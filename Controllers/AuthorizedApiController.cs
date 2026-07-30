using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace back_mylife.Controllers
{
    // Controller ฐานสำหรับ endpoint ที่ผูกกับข้อมูลผู้ใช้รายคน
    // บังคับให้ต้องมี JWT ที่ผ่านการยืนยัน (Authorize) และเปิด CurrentUserId
    // ให้ตรวจสอบความเป็นเจ้าของข้อมูลก่อนอ่าน/แก้ไข/ลบเสมอ แทนที่จะเชื่อ
    // ค่า userId ที่ client ส่งมาทาง route หรือ body ตรงๆ
    [ApiController]
    [Authorize]
    public abstract class AuthorizedApiController : ControllerBase
    {
        protected Guid CurrentUserId
        {
            get
            {
                var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
                return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
            }
        }

        protected bool IsCurrentUser(Guid userId) => userId != Guid.Empty && userId == CurrentUserId;
    }
}
