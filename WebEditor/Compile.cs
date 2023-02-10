using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.JSInterop;
using System.Reflection;

namespace WebEditor;

public class Compile
{
    /// <summary>
    /// 定义需要加载的程序集，相当于项目引用第三方程序集
    /// </summary>
    static List<string> ReferenceAssembly = new(){
        "/_framework/System.dll",
        "/_framework/System.Buffers.dll",
        "/_framework/System.Collections.dll",
        "/_framework/System.Core.dll",
        "/_framework/System.Linq.Expressions.dll",
        "/_framework/System.Linq.Parallel.dll",
        "/_framework/mscorlib.dll",
        "/_framework/System.Linq.dll",
        "/_framework/System.Console.dll",
        "/_framework/System.Private.CoreLib.dll",
        "/_framework/System.Runtime.dll"
    };

    private static IEnumerable<MetadataReference>? _references;
    private static CSharpCompilation _previousCompilation;

    private static object[] _submissionStates = { null, null };
    private static int _submissionIndex = 0;

    /// <summary>
    /// 注入的HttpClient
    /// </summary>
    private static HttpClient Http;

    /// <summary>
    /// 初始化Compile
    /// </summary>
    /// <param name="http"></param>
    /// <returns></returns>
    public static void Init(HttpClient http)
    {
        Http = http;
    }

    [JSInvokable("Execute")]
    public static async Task<string> Execute(string code)
    {
        return await RunSubmission(code);
    }

    private static bool TryCompile(string source, out Assembly? assembly, out IEnumerable<Diagnostic> errorDiagnostics)
    {
        assembly = null;

        var scriptCompilation = CSharpCompilation.CreateScriptCompilation(
            Path.GetRandomFileName(),
            CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script)
                .WithLanguageVersion(LanguageVersion.Preview)), _references,
            // 默认引用的程序集
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: new[]
            {
                "System",
                "System.Collections.Generic",
                "System.Console",
                "System.Diagnostics",
                "System.Dynamic",
                "System.Linq",
                "System.Linq.Expressions",
                "System.Text",
                "System.Threading.Tasks"
            }, concurrentBuild: false),
            _previousCompilation
        );

        errorDiagnostics = scriptCompilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error);
        if (errorDiagnostics.Any())
        {
            return false;
        }

        using var peStream = new MemoryStream();
        var emitResult = scriptCompilation.Emit(peStream);
        if (emitResult.Success)
        {
            _submissionIndex++;
            _previousCompilation = scriptCompilation;
            assembly = Assembly.Load(peStream.ToArray());
            return true;
        }

        return false;
    }

    /// <summary>
    /// 执行Code
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    private static async Task<string> RunSubmission(string code)
    {
        var diagnostic = string.Empty;
        try
        {
            if (_references == null)
            {
                // 定义零时集合
                var references = new List<MetadataReference>(ReferenceAssembly.Count);
                foreach (var reference in ReferenceAssembly)
                {
                    await using var stream = await Http.GetStreamAsync(reference);

                    references.Add(MetadataReference.CreateFromStream(stream));
                }

                _references = references;
            }

            if (TryCompile(code, out var script, out var errorDiagnostics))
            {
                var entryPoint = _previousCompilation.GetEntryPoint(CancellationToken.None);
                var type = script.GetType($"{entryPoint.ContainingNamespace.MetadataName}.{entryPoint.ContainingType.MetadataName}");
                var entryPointMethod = type.GetMethod(entryPoint.MetadataName);

                var submission = (Func<object[], Task>)entryPointMethod.CreateDelegate(typeof(Func<object[], Task>));

                // 如果不进行添加会出现超出索引
                if (_submissionIndex >= _submissionStates.Length)
                {
                    Array.Resize(ref _submissionStates, Math.Max(_submissionIndex, _submissionStates.Length * 2));
                }
                // 执行代码
                _ = await ((Task<object>)submission(_submissionStates));

            }

            diagnostic = string.Join(Environment.NewLine, errorDiagnostics);

        }
        catch (Exception ex)
        {
            diagnostic += Environment.NewLine + ex;
        }
        return diagnostic;
    }
}