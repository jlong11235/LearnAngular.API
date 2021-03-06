using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using DatingApp.API.Data;
using DatingApp.API.Dtos;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DatingApp.API.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthRepository _repository;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        public AuthController(IAuthRepository repository, IConfiguration configuration, IMapper mapper)
        {
            _configuration = configuration;
            _mapper = mapper;
            _repository = repository;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(UserForRegisterDto userForRegisterDto)
        {
            try
            {
                userForRegisterDto.Username = userForRegisterDto.Username.ToLower();
                if (await _repository.UserExists(userForRegisterDto.Username))
                    return BadRequest("Username already exists");

                var userToCreate = _mapper.Map<User>(userForRegisterDto);

                var createdUser = await _repository.Register(userToCreate, userForRegisterDto.Password);

                var userToReturn = _mapper.Map<UserForDetailedDto>(createdUser);

                return CreatedAtRoute("GetUser", new {controller = "Users", id = createdUser.Id}, userToReturn);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return StatusCode(500, "There was an error on registration");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(UserForLoginDto userForLoginDto)
        {
            try
            {
                var userFromRepo = await _repository.Login(userForLoginDto.Username.ToLower(), userForLoginDto.Password);

                if (userFromRepo == null)
                    return Unauthorized();

                var tokenDescriptor = CreateTokenDescriptor(userFromRepo);

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);

                var user = _mapper.Map<UserForListDto>(userFromRepo);       
                return Ok(new
                {
                    token = tokenHandler.WriteToken(token),
                    user
                });

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return StatusCode(500, "There was an error on login");
            }
        }

        private SecurityTokenDescriptor CreateTokenDescriptor(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration.GetSection("AppSettings:Token").Value));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var tokenDescriptor = new SecurityTokenDescriptor()
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(1),
                SigningCredentials = creds
            };
            return tokenDescriptor;
        }
    }
}