using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CutWire.Studio.Api.Pages.Admin;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class AdminPageModel : PageModel
{
    public const string OkKey = "flash-ok";
    public const string ErrorKey = "flash-error";

    protected IActionResult Back(Result result, string done, object? route = null)
    {
        if (result.IsSuccess) TempData[OkKey] = done;
        else TempData[ErrorKey] = result.Error.Message;
        return RedirectToPage(route);
    }

    protected IActionResult Fail(string message, object? route = null)
    {
        TempData[ErrorKey] = message;
        return RedirectToPage(route);
    }
}
