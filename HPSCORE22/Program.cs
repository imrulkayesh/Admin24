//using Admin360.Interface;
//using Admin360.Interface.Meeting;
using Admin360.Repositories;
using Admin360.Services;
using AlphaCare.Interface;
//using AlphaCare.Repositories;
//using AlphaCare.Repositories.CommonRepository;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using QCMS.Repositories;
using QCMS.Services;
using RetailCare.Common;
using RetailCare.Interface;

using RetailCare.Repositories;

using Rotativa.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// Add your DatabaseService and UserRepository
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<CommonService>();
builder.Services.AddScoped<CommonRepository>();


// Newly Added Code
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<ICommonMethod, SessionHelper>();
builder.Services.AddScoped<ICommonServiceMethods, CommonServiceMethods>();
builder.Services.AddScoped<IReportingMethods, ReportingMethods>();
//builder.Services.AddScoped<ICommonCoreRepository, CommonCoreRepository>();
//builder.Services.AddScoped<ISetupRepository, SetupRepository>();
builder.Services.AddScoped<IModuleRepository, ModuleRepository>();
builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
builder.Services.AddScoped<IDispatchRepository, DispatchRepository>();          // Dispatch module (port of YA Pages/Dispatch)
builder.Services.AddSingleton<DispatchAttachmentStore>();                       // Dispatch item attachments (Dispatch:AttachmentFolder)
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();            // Service module (port of YA Pages/Service)
builder.Services.AddSingleton<ServiceAttachmentStore>();                        // Service item attachments (Service:AttachmentFolder)
builder.Services.AddScoped<IRegisterKhataRepository, RegisterKhataRepository>();  // Register Khata module (port of YA Pages/RegisterKhata)
builder.Services.AddHttpClient<HrisStaffService>(c => c.Timeout = TimeSpan.FromSeconds(10)); // HRIS staff check (Register Khata - Issue)

builder.Services.AddScoped<IModuleDashboardProvider, MeetingDashboardProvider>();
builder.Services.AddScoped<IModuleDashboardProvider, DispatchDashboardProvider>();

builder.Services.AddScoped<ILoginLogRepository, LoginLogRepository>();          // USER_LOGIN_LOG (24/7 login activity tracking)
builder.Services.AddScoped<IAdminSetupRepository, AdminSetupRepository>();      // Admin module (port of YA Pages/Admin)
builder.Services.AddScoped<IUserManagementRepository, UserManagementRepository>(); // UserManagement module (port of YA Pages/UserManagement)


builder.Services.AddSingleton<AlphaCare.Services.IEmailService, AlphaCare.Services.EmailService>();

// Add HttpClient and ApiService
builder.Services.AddHttpClient<ApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddMemoryCache();

// ===============================
// Data protection keys  (login cookies stay valid after an app restart / IIS recycle)
// ===============================
// live server: the app pool user needs WRITE permission on this folder and a publish must NOT delete it.
// Best: a folder outside the website, set in appsettings.Production.json:  "DataProtection": { "KeysFolder": "D:\\AppKeys\\Admin247" }
var keysFolder = builder.Configuration["DataProtection:KeysFolder"]
                 ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("Admin247");

// ===============================
// Authentication (Cookie)
// ===============================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Accounts/Login";
        options.AccessDeniedPath = "/Accounts/Denied";

        options.Cookie.Name = "Admin247.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);   // "Keep me signed in" NOT ticked (the login sets 7 days when ticked)
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Add session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

RotativaConfiguration.Setup(
    Path.Combine(app.Environment.WebRootPath, "Rotativa")
);
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UsePathBase("/HPSWeb");
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable session before authorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ===============================
// DEBUG MIDDLEWARE - LOG COOKIES
// ===============================
app.Use(async (context, next) =>
{
    // Log all cookies
    var cookies = context.Request.Cookies;
    Console.WriteLine("=== REQUEST COOKIES ===");
    foreach (var cookie in cookies)
    {
        Console.WriteLine($"Cookie: {cookie.Key} = {cookie.Value}");
    }
    Console.WriteLine("========================");
    await next();
});

app.UseAuthorization();

// ===============================
// SESSION EXPIRY MIDDLEWARE
// ===============================
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower();

    // Skip public paths
    if (path != null &&
        (path.StartsWith("/accounts/login") ||
         path.StartsWith("/accounts/logout") ||
         path.StartsWith("/css") ||
         path.StartsWith("/js") ||
         path.StartsWith("/images")))
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated == true &&
        string.IsNullOrEmpty(context.Session.GetString("USERID")))
    {
        // The session is gone (30 min idle / browser closed / app restarted) but the login cookie is still valid.
        //  - "Keep me signed in" ticked  -> build the session again from the database, the user does not notice
        //  - not ticked                  -> sign out (as before)
        if (!LoginSession.TryRestore(context))
        {
            LoginActivity.OnExpired(context);                       // USER_LOGIN_LOG: close the row as EXPIRED
            await context.SignOutAsync();
            context.Response.Redirect(context.Request.PathBase + "/Accounts/Login");   // PathBase: the live site runs under /HPSWeb
            return;
        }
        LoginActivity.AfterRestore(context);                        // USER_LOGIN_LOG: re-link the restored session to its row
    }

    LoginActivity.TouchIfDue(context);                              // USER_LOGIN_LOG: LAST_ACTIVITY (max one write / 5 min)
    await next();
});
// AREA ROUTE (if you have Areas)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
);

// Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Accounts}/{action=Login}/{id?}"
);

app.MapControllers(); // enable API endpoints

app.Run();
