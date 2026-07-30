using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace back_mylife.Controllers
{
    // ใช้กับ endpoint ที่เรียกโดยบริการภายนอก (เช่น LINE bot worker) ซึ่งไม่มี
    // JWT ผู้ใช้ให้ตรวจสอบ ต้องแนบ header "X-Service-Key" ให้ตรงกับค่า
    // SERVICE_API_KEY ที่ตั้งไว้ใน environment แทน ถ้าไม่ได้ตั้งค่านี้ไว้ที่
    // เซิร์ฟเวอร์ endpoint จะปฏิเสธคำขอทั้งหมด (fail closed) เพื่อไม่ให้เผลอเปิดสาธารณะ
    public class RequireServiceKeyAttribute : ActionFilterAttribute
    {
        private const string HeaderName = "X-Service-Key";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expectedKey = configuration["Service:ApiKey"];

            if (string.IsNullOrEmpty(expectedKey))
            {
                context.Result = new ObjectResult(new { message = "Service API key is not configured on the server." })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                return;
            }

            var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrEmpty(providedKey) || providedKey != expectedKey)
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
