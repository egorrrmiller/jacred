using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Models.AppConf;
using Newtonsoft.Json;

namespace JacRed.Core.Utils;

public static class HttpClient
{
	private static string useragent =>
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

	#region Get

	public static async ValueTask<string> Get(string url, Encoding encoding = default, string cookie = null,
											string referer = null, int timeoutSeconds = 15,
											List<(string name, string val)> addHeaders = null,
											long MaxResponseContentBufferSize = 0, bool useproxy = false, WebProxy proxy = null,
											int httpversion = 1) => (await BaseGetAsync(url, encoding, cookie, referer, timeoutSeconds,
		addHeaders: addHeaders,
		MaxResponseContentBufferSize: MaxResponseContentBufferSize, useproxy: useproxy, proxy: proxy,
		httpversion: httpversion)).content;

	#endregion

	#region Get<T>

	public static async ValueTask<T> Get<T>(string url, Encoding encoding = default, string cookie = null,
											string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15,
											List<(string name, string val)> addHeaders = null, bool IgnoreDeserializeObject = false,
											bool useproxy = false,
											WebProxy proxy = null)
	{
		try
		{
			var html = (await BaseGetAsync(url, encoding, cookie, referer,
				MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds,
				addHeaders: addHeaders, useproxy: useproxy, proxy: proxy)).content;

			if (html == null)
			{
				return default;
			}

			if (IgnoreDeserializeObject)
			{
				return JsonConvert.DeserializeObject<T>(html,
					new JsonSerializerSettings
					{
						Error = (se, ev) =>
						{
							ev.ErrorContext.Handled = true;
						}
					});
			}

			return JsonConvert.DeserializeObject<T>(html);
		}
		catch
		{
			return default;
		}
	}

	#endregion

	#region BaseGetAsync

	public static async ValueTask<(string content, HttpResponseMessage response)> BaseGetAsync(string url,
		Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15,
		long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, bool useproxy = false,
		WebProxy proxy = null, int httpversion = 1)
	{
		try
		{
			var handler = new HttpClientHandler
			{
				AutomaticDecompression = DecompressionMethods.GZip|DecompressionMethods.Deflate
			};

			handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

			#region proxy

			if (AppInit.conf.proxy.list != null && AppInit.conf.proxy.list.Count > 0 && useproxy)
			{
				handler.UseProxy = true;
				handler.Proxy = proxy ?? webProxy();
			}

			if (AppInit.conf.globalproxy != null && AppInit.conf.globalproxy.Count > 0)
			{
				foreach (var p in AppInit.conf.globalproxy)
				{
					if (p.list == null || p.list.Count == 0)
					{
						continue;
					}

					if (Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
					{
						handler.UseProxy = true;
						handler.Proxy = webProxy(p);

						break;
					}
				}
			}

			#endregion

			using (var client = new System.Net.Http.HttpClient(handler))
			{
				client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

				client.MaxResponseContentBufferSize =
					MaxResponseContentBufferSize == 0
						? 10_000_000
						: MaxResponseContentBufferSize; // 10MB

				client.DefaultRequestHeaders.Add("user-agent", useragent);

				if (cookie != null)
				{
					client.DefaultRequestHeaders.Add("cookie", cookie);
				}

				if (referer != null)
				{
					client.DefaultRequestHeaders.Add("referer", referer);
				}

				if (addHeaders != null)
				{
					foreach (var item in addHeaders)
					{
						client.DefaultRequestHeaders.Add(item.name, item.val);
					}
				}

				var req = new HttpRequestMessage(HttpMethod.Get, url)
				{
					Version = new(httpversion, 0)
				};

				using (var response = await client.SendAsync(req))
				{
					if (response.StatusCode != HttpStatusCode.OK)
					{
						return (null, response);
					}

					using (var content = response.Content)
					{
						if (encoding != default)
						{
							var res = encoding.GetString(await content.ReadAsByteArrayAsync());

							if (string.IsNullOrWhiteSpace(res))
							{
								return (null, response);
							}

							return (res, response);
						} else
						{
							var res = await content.ReadAsStringAsync();

							if (string.IsNullOrWhiteSpace(res))
							{
								return (null, response);
							}

							return (res, response);
						}
					}
				}
			}
		}
		catch
		{
			return (null,
				new()
				{
					StatusCode = HttpStatusCode.InternalServerError,
					RequestMessage = new()
				});
		}
	}

	#endregion

	#region Download

	public static async ValueTask<byte[]> Download(string url, string cookie = null, string referer = null,
													int timeoutSeconds = 30, long MaxResponseContentBufferSize = 0,
													List<(string name, string val)> addHeaders = null, bool useproxy = false,
													WebProxy proxy = null)
	{
		try
		{
			var handler = new HttpClientHandler
			{
				AllowAutoRedirect = true,
				AutomaticDecompression = DecompressionMethods.Brotli|DecompressionMethods.GZip|DecompressionMethods.Deflate
			};

			handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

			#region proxy

			if (AppInit.conf.proxy.list != null && AppInit.conf.proxy.list.Count > 0 && useproxy)
			{
				handler.UseProxy = true;
				handler.Proxy = proxy ?? webProxy();
			}

			if (AppInit.conf.globalproxy != null && AppInit.conf.globalproxy.Count > 0)
			{
				foreach (var p in AppInit.conf.globalproxy)
				{
					if (p.list == null || p.list.Count == 0)
					{
						continue;
					}

					if (Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
					{
						handler.UseProxy = true;
						handler.Proxy = webProxy(p);

						break;
					}
				}
			}

			#endregion

			using (var client = new System.Net.Http.HttpClient(handler))
			{
				client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

				client.MaxResponseContentBufferSize =
					MaxResponseContentBufferSize == 0
						? 10_000_000
						: MaxResponseContentBufferSize; // 10MB

				client.DefaultRequestHeaders.Add("user-agent", useragent);

				if (cookie != null)
				{
					client.DefaultRequestHeaders.Add("cookie", cookie);
				}

				if (referer != null)
				{
					client.DefaultRequestHeaders.Add("referer", referer);
				}

				if (addHeaders != null)
				{
					foreach (var item in addHeaders)
					{
						client.DefaultRequestHeaders.Add(item.name, item.val);
					}
				}

				using (var response = await client.GetAsync(url))
				{
					if (response.StatusCode != HttpStatusCode.OK)
					{
						return null;
					}

					using (var content = response.Content)
					{
						var res = await content.ReadAsByteArrayAsync();

						if (res.Length == 0)
						{
							return null;
						}

						return res;
					}
				}
			}
		}
		catch
		{
			return null;
		}
	}

	#endregion

	#region webProxy

	private static readonly ConcurrentBag<string> proxyRandomList = new();

	public static WebProxy webProxy()
	{
		if (proxyRandomList.Count == 0)
		{
			foreach (var ip in AppInit.conf.proxy.list.OrderBy(a => Guid.NewGuid()))
			{
				proxyRandomList.Add(ip);
			}
		}

		proxyRandomList.TryTake(out var proxyip);

		ICredentials credentials = null;

		if (AppInit.conf.proxy.useAuth)
		{
			credentials = new NetworkCredential(AppInit.conf.proxy.username, AppInit.conf.proxy.password);
		}

		return new(proxyip, AppInit.conf.proxy.BypassOnLocal, null, credentials);
	}

	private static WebProxy webProxy(ProxySettings p)
	{
		ICredentials credentials = null;

		if (p.useAuth)
		{
			credentials = new NetworkCredential(p.username, p.password);
		}

		return new(p.list.OrderBy(a => Guid.NewGuid())
			.First(), p.BypassOnLocal, null, credentials);
	}

	#endregion

	#region Post

	public static ValueTask<string> Post(string url, string data, string cookie = null,
										int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15,
										List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null) =>
		Post(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie,
			MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds,
			addHeaders: addHeaders, useproxy: useproxy, proxy: proxy);

	public static async ValueTask<string> Post(string url, HttpContent data, Encoding encoding = default,
												string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15,
												List<(string name, string val)> addHeaders = null, bool useproxy = false,
												WebProxy proxy = null)
	{
		try
		{
			var handler = new HttpClientHandler
			{
				AutomaticDecompression = DecompressionMethods.Brotli|DecompressionMethods.GZip|DecompressionMethods.Deflate
			};

			handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

			#region proxy

			if (AppInit.conf.proxy.list != null && AppInit.conf.proxy.list.Count > 0 && useproxy)
			{
				handler.UseProxy = true;
				handler.Proxy = proxy ?? webProxy();
			}

			if (AppInit.conf.globalproxy != null && AppInit.conf.globalproxy.Count > 0)
			{
				foreach (var p in AppInit.conf.globalproxy)
				{
					if (p.list == null || p.list.Count == 0)
					{
						continue;
					}

					if (Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
					{
						handler.UseProxy = true;
						handler.Proxy = webProxy(p);

						break;
					}
				}
			}

			#endregion

			using (var client = new System.Net.Http.HttpClient(handler))
			{
				client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

				client.MaxResponseContentBufferSize =
					MaxResponseContentBufferSize != 0
						? MaxResponseContentBufferSize
						: 10_000_000; // 10MB

				client.DefaultRequestHeaders.Add("user-agent", useragent);

				if (cookie != null)
				{
					client.DefaultRequestHeaders.Add("cookie", cookie);
				}

				if (addHeaders != null)
				{
					foreach (var item in addHeaders)
					{
						client.DefaultRequestHeaders.Add(item.name, item.val);
					}
				}

				using (var response = await client.PostAsync(url, data))
				{
					if (response.StatusCode != HttpStatusCode.OK)
					{
						return null;
					}

					using (var content = response.Content)
					{
						if (encoding != default)
						{
							var res = encoding.GetString(await content.ReadAsByteArrayAsync());

							if (string.IsNullOrWhiteSpace(res))
							{
								return null;
							}

							return res;
						} else
						{
							var res = await content.ReadAsStringAsync();

							if (string.IsNullOrWhiteSpace(res))
							{
								return null;
							}

							return res;
						}
					}
				}
			}
		}
		catch
		{
			return null;
		}
	}

	#endregion

	#region Post<T>

	public static async ValueTask<T> Post<T>(string url, string data, string cookie = null, int timeoutSeconds = 15,
											List<(string name, string val)> addHeaders = null, bool useproxy = false,
											Encoding encoding = default,
											WebProxy proxy = null, bool IgnoreDeserializeObject = false) => await Post<T>(url,
		new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie,
		timeoutSeconds, addHeaders, useproxy, encoding, proxy,
		IgnoreDeserializeObject);

	public static async ValueTask<T> Post<T>(string url, HttpContent data, string cookie = null,
											int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null,
											bool useproxy = false,
											Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
	{
		try
		{
			var json = await Post(url, data, cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders,
				useproxy: useproxy, encoding: encoding, proxy: proxy);

			if (json == null)
			{
				return default;
			}

			if (IgnoreDeserializeObject)
			{
				return JsonConvert.DeserializeObject<T>(json,
					new JsonSerializerSettings
					{
						Error = (se, ev) =>
						{
							ev.ErrorContext.Handled = true;
						}
					});
			}

			return JsonConvert.DeserializeObject<T>(json);
		}
		catch
		{
			return default;
		}
	}

	#endregion
}