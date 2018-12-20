using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DatingApp.API.Data;
using DatingApp.API.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DatingApp.API.Dtos;
using DatingApp.API.Models;

namespace DatingApp.API.Controllers
{
    [Authorize]
    [Route("api/users/{userId}/photos")]
    [ApiController]
    public class PhotosController: ControllerBase
    {
        private readonly IDatingRepository _repo;
        private readonly IMapper _mapper;
        private readonly IOptions<CloudinarySettings> _cloudinaryConfig;
        private Cloudinary _cloudinary;

        public PhotosController(IDatingRepository repo, IMapper mapper, IOptions<CloudinarySettings> cloudinaryConfig)
        {
            _repo = repo;
            _mapper = mapper;
            _cloudinaryConfig = cloudinaryConfig;
            
            Account acc = new Account(
                _cloudinaryConfig.Value.CloudName,
                _cloudinaryConfig.Value.ApiKey,
                _cloudinaryConfig.Value.ApiSecret
             );
            
            _cloudinary = new Cloudinary(acc);
        }

        [HttpGet("{id}", Name = "GetPhoto")]
        public async Task<IActionResult> GetPhoto(int id)
        {
            var photoFromRepo = await _repo.GetPhoto(id);
            var photo = _mapper.Map<PhotoForReturnDto>(photoFromRepo);

            return Ok(photo);
        }

        [HttpPost]
        public async Task<IActionResult> AddPhotoForUser(int userId, [FromForm]PhotoForCreationDto photoForCreationDto)
        {
            if (!IsUserValid(userId)) return Unauthorized();

            var userFromRepo = await _repo.GetUser(userId);

            var file = photoForCreationDto.File;
            var uploadResults = new ImageUploadResult();

            if (file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(file.Name, stream),
                        Transformation = new Transformation().Width(500).Height(500).Crop("fill").Gravity("face")
                    };

                    uploadResults = _cloudinary.Upload(uploadParams);
                }
            }

            photoForCreationDto.Url = uploadResults.Uri.ToString();
            photoForCreationDto.PublicId = uploadResults.PublicId;

            var photo = _mapper.Map<Photo>(photoForCreationDto);

            if (!userFromRepo.Photos.Any(u => u.IsMain))
                photo.IsMain = true;
            
            userFromRepo.Photos.Add(photo);

            if (await _repo.SaveAll())
            {
                var photoToReturn = _mapper.Map<PhotoForReturnDto>(photo);

                return CreatedAtRoute("GetPhoto", new { id = photo.Id}, photoToReturn);
            }

            return BadRequest("Could not add the photo");
        }

        [HttpPost("{photoId}/setMain")]
        public async Task<IActionResult> SetMainPhoto(int userId, int photoId)
        {
            if (!IsUserValid(userId)) return Unauthorized();

            if (!await IsUsersPhoto(userId, photoId)) return Unauthorized();

            var photoFromRepo = await _repo.GetPhoto(photoId);
            if (photoFromRepo.IsMain) return BadRequest("Photo is already main photo");

            var currentMainPhoto = await _repo.GetMainPhotoForUser(userId);
            currentMainPhoto.IsMain = false;

            photoFromRepo.IsMain = true;

            if (await _repo.SaveAll())
                return NoContent();

            return BadRequest("Could not set photo to main");
        }

        [HttpDelete("{photoId}")]
        public async Task<IActionResult> DeletePhoto(int userId, int photoId)
        {
            if (!IsUserValid(userId)) return Unauthorized();

            if (!await IsUsersPhoto(userId, photoId)) return Unauthorized();

            var photoFromRepo = await _repo.GetPhoto(photoId);
            if (photoFromRepo.IsMain) return BadRequest("You can not delete your main photo");

            if (photoFromRepo.PublicId != null)
            {
                var deleteParams = new DeletionParams(photoFromRepo.PublicId);
                var result = _cloudinary.Destroy(deleteParams);

                if (result.Result == "ok")
                {
                    _repo.Delete(photoFromRepo);
                }    
            }

            if (photoFromRepo.PublicId == null)
            {
                _repo.Delete(photoFromRepo);
            }
            if (await _repo.SaveAll())
            {
                return Ok();
            }

            return BadRequest("Failed to delete the photo");
        }

        private async Task<bool> IsUsersPhoto(int userId, int photoId)
        {
            var user = await _repo.GetUser(userId);
            return user.Photos.Any(p => p.Id == photoId);
        }

        private bool IsUserValid(int userId)
        {
            return userId == int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }
    }
}