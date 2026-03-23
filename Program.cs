using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MasterServer.Models;
using MasterServer;

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["Urls"];
if (!string.IsNullOrEmpty(urls))
	builder.WebHost.UseUrls(urls);

builder.Services.AddDbContext<MasterDbContext>(options =>
	options.UseSqlite("Data Source=masterserver.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(options =>
	{
		options.LoginPath = "/login";
		options.LogoutPath = "/logout";
	});

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
	db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapAllEndpoints();

app.Run();

