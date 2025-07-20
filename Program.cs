using Microsoft.EntityFrameworkCore;
using SignalRChat.Data;
using SignalRChat.Hubs;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddDbContext<ChatDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("ChatDatabase")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapRazorPages();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

Process.Start(new ProcessStartInfo
{
    FileName = "http://localhost:5039/chat",
    UseShellExecute = true
});

app.Run();