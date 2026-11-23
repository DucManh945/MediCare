using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;

namespace MediCare
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

        protected void Application_PostAuthenticateRequest(object sender, EventArgs e)
        {
            var identity = HttpContext.Current.User?.Identity as FormsIdentity;
            if (identity == null || !identity.IsAuthenticated)
            {
                return;
            }

            var userData = identity.Ticket.UserData ?? string.Empty;
            var parts = userData.Split(new[] { '|' }, 3);
            var roleName = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(roleName))
            {
                FormsAuthentication.SignOut();
                HttpContext.Current.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                roleName
            };

            if (roleName.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                roleName.IndexOf("quản trị", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                roles.Add("Admin");
                roles.Add("Admin hệ thống");
                roles.Add("Quản trị viên");
            }

            if (roleName.IndexOf("quản lý", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                roles.Add("Quản lý");
                roles.Add("Quản lý phòng khám");
            }

            if (roleName.IndexOf("bác sĩ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                roleName.IndexOf("bac si", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                roles.Add("Bác sĩ");
            }

            HttpContext.Current.User = new GenericPrincipal(identity, new List<string>(roles).ToArray());
        }
    }
}
