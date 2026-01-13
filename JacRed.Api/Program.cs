using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Api.Configuration;
using JacRed.Api.Controllers;
using JacRed.Api.Engine;
using JacRed.Api.Engine.Tracks;
using JacRed.Api.HostedServices;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// --- Kestrel ---
builder.WebHost.UseKestrel(op =>
{
	var ip = AppInit.conf.listenip == "any"
		? IPAddress.Any
		: IPAddress.Parse(AppInit.conf.listenip);

	op.Listen(ip, AppInit.conf.listenport);
});

// --- Подготовка директорий ---
Directory.CreateDirectory("Data/fdb");
Directory.CreateDirectory("Data/temp");
Directory.CreateDirectory("Data/log");
Directory.CreateDirectory("Data/tracks");

// --- Инициализация ---
TracksDB.Configuration();
	/*SyncController.Configuration();
ApiController.getFastdb(true);*/

// --- Cron / Long Running Jobs ---
/*ThreadPool.QueueUserWorkItem(async _ =>
{
	while (true)
	{
		await Task.Delay(TimeSpan.FromMinutes(10));

		try
		{
			ApiController.getFastdb(true);
		}
		catch
		{
		}
	}
});*/

builder.Services.RegisterServices();

// --- Culture / Encoding ---
CultureInfo.CurrentCulture = new("ru-RU");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Services ---
builder.Services.Configure<CookiePolicyOptions>(options =>
{
	options.CheckConsentNeeded = context => true;
	options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddResponseCompression(options =>
{
	options.MimeTypes =
		ResponseCompressionDefaults.MimeTypes.Concat(new[]
		{
			"application/vnd.apple.mpegurl",
			"image/svg+xml"
		});
});

builder.Services.AddControllersWithViews()
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
		options.JsonSerializerOptions.PropertyNamingPolicy = null;
	});

builder.Services.AddHostedService<CacheInitializer>();

// --- Build app ---
var app = builder.Build();

var torrent = app.Services.GetRequiredService<ITorrentRepository>();
var content = app.Services.GetRequiredService<IContentCatalog>();

ThreadPool.QueueUserWorkItem(async _ => await new SyncCron(content, torrent).Torrents());
ThreadPool.QueueUserWorkItem(async _ => await new SyncCron(content, torrent).Spidr());
ThreadPool.QueueUserWorkItem(async _ => await new TrackersCron(torrent, content).Run());
ThreadPool.QueueUserWorkItem(async _ => await new StatsCron(content, torrent).Run());

for (var i = 1; i <= 5; i++)
{
	ThreadPool.QueueUserWorkItem(async _ => await new TracksCron(torrent, content).Run(i));
}

// --- Middleware ---
app.UseDeveloperExceptionPage();

app.UseForwardedHeaders(new()
{
	ForwardedHeaders = ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseResponseCompression();

if (AppInit.conf.web)
{
	app.UseStaticFiles();
}

app.UseModHeaders();

// --- Endpoints ---
app.MapControllers();

// --- Start ---
app.Run();