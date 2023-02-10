using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebEditor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

var app = builder.Build();

// ��ȡHttpClient���ݵ���ʼ������
Compile.Init(app.Services.GetRequiredService<HttpClient>());

await app.RunAsync();