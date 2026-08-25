using System.Security.Claims;
using appcore.Services;
using backend.Errors;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>
    /// Return information about users
    /// </summary>
    [Route("api/userinfo")]
    [ApiController]
    public class UserInfoController(UserService users) : ControllerBase
    {
        /// <summary>
        /// An object describing a current user
        /// </summary>
        public class UserInfo
        {
            /// <summary>
            /// my user id
            /// </summary>
            required public string Id { get; init; }
            /// <summary>
            /// display name
            /// </summary>
            required public string Name { get; init; }
            /// <summary>
            /// Email address
            /// </summary>
            public string? Email { get; init; }

            /// <summary>
            /// Profile picture url
            /// </summary>
            public string? ProfilePictureUrl { get; init; }
        }

        /// <summary>
        /// An object describing the current user.
        /// </summary>
        public class Me
        {
            /// <summary>
            /// The current user.
            /// </summary>
            public required UserInfo Userinfo { get; set; }

            /// <summary>
            /// A boolean detailing if the current user can start and manage auctions
            /// </summary>
            public bool CanStartAuctions { get; set; }
        }

        /// <summary>
        /// Return information about the currently signed in user.
        /// </summary>
        [HttpGet("me")]
        public async Task<Me> GetMe()
        {
            var me_claim = User.FindFirstValue("local_userid") ?? throw new ForbidException();
            if (!Guid.TryParse(me_claim, out var userId))
                throw new ForbidException();

            var me = await users.GetUserById(userId) ?? throw new ForbidException();

            var can_start_auctions = User.FindAll("mercury.role").Any(c => c.Value == "Admin");

            return new Me()
            {
                Userinfo = new()
                {
                    Id = me_claim,
                    Name = me.Name,
                    Email = me.Email,
                    ProfilePictureUrl = me.ProfilePictureUrl,
                },
                CanStartAuctions = can_start_auctions,
            };
        }

    }
}
