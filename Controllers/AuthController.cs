using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using back_mylife.Services;
using BCrypt.Net;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class AuthController : AuthorizedApiController
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly OracleObjectStorageService _storageService;
        private readonly ILogger<AuthController> _logger;

        private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB

        public AuthController(
            AppDbContext context, 
            IConfiguration configuration,
            OracleObjectStorageService storageService,
            ILogger<AuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _storageService = storageService;
            _logger = logger;
        }

        public record RegisterDto(string Email, string Password, string FullName);
        public record LoginDto(string Email, string Password);
        public record SocialLoginDto(string Provider, string ProviderId, string Email, string FullName);
        public record UpdateProfileDto(string FullName);
        public record ChangePasswordDto(string? CurrentPassword, string NewPassword);

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            {
                return BadRequest(new { message = "อีเมลนี้ถูกใช้งานแล้ว" });
            }

            var user = new User
            {
                Email = dto.Email,
                FullName = dto.FullName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var token = JwtTokenService.GenerateToken(user.Id.ToString(), user.Email, user.FullName, _configuration);
            return Ok(new { 
                message = "ลงทะเบียนสำเร็จ", 
                token, 
                userId = user.Id, 
                email = user.Email, 
                fullName = user.FullName,
                profileImageUrl = user.ProfileImageUrl
            });
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง" });
            }

            var token = JwtTokenService.GenerateToken(user.Id.ToString(), user.Email, user.FullName, _configuration);
            return Ok(new { 
                message = "เข้าสู่ระบบสำเร็จ", 
                token, 
                userId = user.Id, 
                email = user.Email, 
                fullName = user.FullName,
                profileImageUrl = user.ProfileImageUrl
            });
        }

        [AllowAnonymous]
        [HttpPost("social-login")]
        public async Task<IActionResult> SocialLogin([FromBody] SocialLoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => 
                u.Email == dto.Email || 
                (dto.Provider.ToLower() == "google" && u.GoogleId == dto.ProviderId) ||
                (dto.Provider.ToLower() == "line" && u.LineId == dto.ProviderId));

            if (user == null)
            {
                user = new User
                {
                    Email = dto.Email,
                    FullName = dto.FullName,
                    GoogleId = dto.Provider.ToLower() == "google" ? dto.ProviderId : null,
                    LineId = dto.Provider.ToLower() == "line" ? dto.ProviderId : null
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (dto.Provider.ToLower() == "google" && user.GoogleId == null) user.GoogleId = dto.ProviderId;
                if (dto.Provider.ToLower() == "line" && user.LineId == null) user.LineId = dto.ProviderId;
                await _context.SaveChangesAsync();
            }

            var token = JwtTokenService.GenerateToken(user.Id.ToString(), user.Email, user.FullName, _configuration);
            return Ok(new { 
                message = $"เข้าสู่ระบบผ่าน {dto.Provider} สำเร็จ", 
                token, 
                userId = user.Id, 
                email = user.Email, 
                fullName = user.FullName,
                profileImageUrl = user.ProfileImageUrl
            });
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            return Ok(new { 
                userId = user.Id, 
                email = user.Email, 
                fullName = user.FullName, 
                profileImageUrl = user.ProfileImageUrl,
                hasGoogle = user.GoogleId != null, 
                hasLine = user.LineId != null,
                hasPassword = !string.IsNullOrEmpty(user.PasswordHash),
                oracleStorageConfigured = _storageService.IsConfigured
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (string.IsNullOrWhiteSpace(dto.FullName))
            {
                return BadRequest(new { message = "กรุณาระบุชื่อ-นามสกุล" });
            }

            user.FullName = dto.FullName.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "อัปเดตโปรไฟล์สำเร็จ", 
                userId = user.Id, 
                email = user.Email, 
                fullName = user.FullName,
                profileImageUrl = user.ProfileImageUrl
            });
        }

        [HttpPost("profile-image")]
        [RequestSizeLimit(MaxImageSizeBytes)]
        public async Task<IActionResult> UploadProfileImage(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "กรุณาเลือกไฟล์รูปภาพที่ต้องการอัปโหลด" });
            }

            if (file.Length > MaxImageSizeBytes)
            {
                return BadRequest(new { message = "ขนาดไฟล์ต้องไม่เกิน 5MB" });
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !AllowedImageExtensions.Contains(extension))
            {
                return BadRequest(new { message = "รองรับเฉพาะไฟล์รูปภาพนามสกุล .jpg, .jpeg, .png, .webp, .gif เท่านั้น" });
            }

            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (!_storageService.IsConfigured)
            {
                return BadRequest(new { message = "ระบบ Oracle Cloud Object Storage ยังไม่ได้กำหนดค่า กรุณาตรวจสอบ .env" });
            }

            using var stream = file.OpenReadStream();
            var (success, url, errorMessage) = await _storageService.UploadProfileImageAsync(
                user.Id, 
                stream, 
                file.ContentType ?? "image/jpeg", 
                extension, 
                user.ProfileImageUrl);

            if (!success || string.IsNullOrEmpty(url))
            {
                return StatusCode(500, new { message = errorMessage ?? "อัปโหลดรูปภาพไม่สำเร็จ" });
            }

            user.ProfileImageUrl = url;
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "อัปโหลดรูปโปรไฟล์สำเร็จ", 
                profileImageUrl = user.ProfileImageUrl 
            });
        }

        [HttpDelete("profile-image")]
        public async Task<IActionResult> DeleteProfileImage()
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            if (!string.IsNullOrEmpty(user.ProfileImageUrl))
            {
                await _storageService.DeleteProfileImageAsync(user.ProfileImageUrl);
                user.ProfileImageUrl = null;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "ลบรูปโปรไฟล์สำเร็จ", profileImageUrl = (string?)null });
        }

        [AllowAnonymous]
        [HttpGet("profile-image/{userId:guid}")]
        public async Task<IActionResult> GetProfileImage(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ProfileImageUrl))
            {
                return NotFound(new { message = "ไม่พบรูปโปรไฟล์ของผู้ใช้นี้" });
            }

            // If public URL, redirect directly
            if (user.ProfileImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                user.ProfileImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(user.ProfileImageUrl);
            }

            // Otherwise stream from OCI object key
            var (stream, contentType) = await _storageService.GetObjectStreamAsync(user.ProfileImageUrl);
            if (stream == null)
            {
                return NotFound(new { message = "ไม่พบไฟล์รูปภาพใน Storage" });
            }

            return File(stream, contentType);
        }

        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var user = await _context.Users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();

            bool hasPassword = !string.IsNullOrEmpty(user.PasswordHash);

            if (hasPassword)
            {
                if (string.IsNullOrEmpty(dto.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                {
                    return BadRequest(new { message = "รหัสผ่านปัจจุบันไม่ถูกต้อง" });
                }
            }

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            {
                return BadRequest(new { message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร" });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = hasPassword ? "เปลี่ยนรหัสผ่านสำเร็จ" : "ตั้งรหัสผ่านสำเร็จ", hasPassword = true });
        }
    }
}
