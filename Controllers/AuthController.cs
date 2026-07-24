using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using back_mylife.Services;
using BCrypt.Net;

namespace back_mylife.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public record RegisterDto(string Email, string Password, string FullName);
        public record LoginDto(string Email, string Password);
        public record SocialLoginDto(string Provider, string ProviderId, string Email, string FullName);

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
            return Ok(new { message = "ลงทะเบียนสำเร็จ", token, userId = user.Id, email = user.Email, fullName = user.FullName });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง" });
            }

            var token = JwtTokenService.GenerateToken(user.Id.ToString(), user.Email, user.FullName, _configuration);
            return Ok(new { message = "เข้าสู่ระบบสำเร็จ", token, userId = user.Id, email = user.Email, fullName = user.FullName });
        }

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
            return Ok(new { message = $"เข้าสู่ระบบผ่าน {dto.Provider} สำเร็จ", token, userId = user.Id, email = user.Email, fullName = user.FullName });
        }
    }
}
