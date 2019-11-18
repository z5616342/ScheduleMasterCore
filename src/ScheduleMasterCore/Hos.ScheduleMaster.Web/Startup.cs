using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hos.ScheduleMaster.Core;
using Hos.ScheduleMaster.Core.Models;
using Hos.ScheduleMaster.Core.Repository;
using Hos.ScheduleMaster.Web.Extension;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pomelo.EntityFrameworkCore.MySql;

namespace Hos.ScheduleMaster.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddOptions();
            services.AddHttpContextAccessor();
            //services.AddControllersWithViews();
            //services.AddControllers();
            services.AddHosControllers(this);
            services.AddMvc(options =>
            {
                //options.OutputFormatters.Add(new SystemTextJsonOutputFormatter(
                //    new System.Text.Json.JsonSerializerOptions
                //    {

                //    }
                //    ));
            }).AddJsonOptions(option =>
            {
                ////忽略循环引用
                //option.JsonSerializerOptions.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                ////不使用驼峰样式的key
                //option.SerializerSettings.ContractResolver = new DefaultContractResolver();
                ////设置时间格式
                //option.SerializerSettings.DateFormatString = "yyyy-MM-dd";
            });

            //配置authorrize
            services.AddAuthentication(b =>
            {
                b.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                b.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                b.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(b =>
            {
                b.LoginPath = "/login";
                b.Cookie.Name = "msc_auth_name";
                b.Cookie.Path = "/";
                b.Cookie.HttpOnly = true;
                //b.Cookie.Expiration = new TimeSpan(2, 0, 0);
                b.ExpireTimeSpan = new TimeSpan(2, 0, 0);
            });
            //EF数据库上下文
            services.AddDbContext<TaskDbContext>(option => option.UseMySql(Configuration.GetConnectionString("MysqlConnection")));

            //注入Uow依赖
            services.AddScoped<IUnitOfWork, UnitOfWork<TaskDbContext>>();
            //自动注册所有业务service
            services.AddAppServices();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseCookiePolicy();
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();
            app.UseAuthentication();

            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapControllers();
                endpoints.MapControllerRoute(
                     name: "default",
                     pattern: "{controller=Login}/{action=Index}/{id?}");
            });

            //加载全局缓存
            ConfigurationCache.RootServiceProvider = app.ApplicationServices;
            ConfigurationCache.Refresh();
            //using (var serviceScope = app.ApplicationServices.CreateScope())
            //{
            //    var context = serviceScope.ServiceProvider.GetService<Core.Repository.IUnitOfWork>();

            //    // Seed the database.
            //}
            //var sd = app.ApplicationServices.GetRequiredService<Core.Models.TaskDbContext>();
        }
    }

    public class HosControllerActivator : IControllerActivator
    {
        public object Create(ControllerContext actionContext)
        {
            var controllerType = actionContext.ActionDescriptor.ControllerTypeInfo.AsType();
            var instance = actionContext.HttpContext.RequestServices.GetRequiredService(controllerType);
            PropertyActivate(instance, actionContext.HttpContext.RequestServices);
            return instance;
        }

        public virtual void Release(ControllerContext context, object controller)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }
            if (controller is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private void PropertyActivate(object service, IServiceProvider provider)
        {
            var serviceType = service.GetType();
            var properties = serviceType.GetProperties().AsEnumerable().Where(x => x.Name.StartsWith("_"));
            foreach (PropertyInfo property in properties)
            {
                var autowiredAttr = property.GetCustomAttribute<AutowiredAttribute>();
                if (autowiredAttr != null)
                {
                    //从DI容器获取实例
                    var innerService = provider.GetService(property.PropertyType);
                    if (innerService != null)
                    {
                        //递归解决服务嵌套问题
                        PropertyActivate(innerService, provider);
                        //属性赋值
                        property.SetValue(service, innerService);
                    }
                }
            }
        }
    }
}
