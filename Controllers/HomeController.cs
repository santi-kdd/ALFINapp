using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ALFINapp.Models;

namespace ALFINapp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (TempData["Message"]!=null)
        {
            TempData["Message"] = TempData["Message"] + "Retornar a Index";
        }
        return View("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult Addingclient()
    {
        return View("Addingclient");
    }

    public IActionResult Error404()
    {
        return View();
    }
}
