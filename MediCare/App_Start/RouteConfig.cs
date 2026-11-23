using System.Web.Mvc;
using System.Web.Routing;

namespace MediCare
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "ClinicManagerMedicine",
                url: "ClinicManager/QuanLyThuoc",
                defaults: new { controller = "ClinicManager", action = "QuanLyThuoc" }
            );

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
