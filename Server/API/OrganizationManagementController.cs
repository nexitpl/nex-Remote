using MailKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using nexRemote.Server.Auth;
using nexRemote.Server.Services;
using nexRemote.Shared.Models;
using nexRemote.Shared.ViewModels;
using Org.BouncyCastle.Crypto.Agreement;
using System.Text.Encodings.Web;
using System.Text;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace nexRemote.Server.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrganizationManagementController : ControllerBase
    {
        public OrganizationManagementController(IDataService dataService,
            UserManager<nexRemoteUser> userManager,
            IEmailSenderEx emailSender)
        {
            DataService = dataService;
            UserManager = userManager;
            EmailSender = emailSender;
        }

        private IDataService DataService { get; }
        private IEmailSenderEx EmailSender { get; }
        private UserManager<nexRemoteUser> UserManager { get; }


        [HttpPost("ChangeIsAdmin/{userID}")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult ChangeIsAdmin(string userID, [FromBody] bool isAdmin)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            if (User.Identity.IsAuthenticated &&
                DataService.GetUserByNameWithOrg(User.Identity.Name).Id == userID)
            {
                return BadRequest("Nie możesz odebrać uprawnień administratora samemu sobie.");
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);

            DataService.ChangeUserIsAdmin(orgID, userID, isAdmin);
            return Ok("ok");
        }

        [HttpDelete("DeleteInvite/{inviteID}")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult DeleteInvite(string inviteID)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            DataService.DeleteInvite(orgID, inviteID);
            return Ok("ok");
        }

        [HttpDelete("DeleteUser/{userID}")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public async Task<IActionResult> DeleteUser(string userID)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            if (User.Identity.IsAuthenticated &&
              DataService.GetUserByNameWithOrg(User.Identity.Name).Id == userID)
            {
                return BadRequest("Nie możesz tutaj usunąć swojego konta.  Przejdź do strony Dane Personalne aby usunąć swoje konto.");
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            await DataService.DeleteUser(orgID, userID);
            return Ok("ok");
        }

        [HttpGet("DeviceGroup")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult DeviceGroup()
        {
            if (User.Identity.IsAuthenticated &&
                DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Ok(DataService.GetDeviceGroups(User.Identity.Name));
            }
            if (Request.Headers.TryGetValue("OrganizationID", out var orgID))
                return Ok(DataService.GetDeviceGroupsForOrganization(orgID));
            return NotFound("Unable to find User or organizationID for device group");
        }

        [HttpDelete("DeviceGroup")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult DeviceGroup([FromBody] string deviceGroupID)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            DataService.DeleteDeviceGroup(orgID, deviceGroupID.Trim());
            return Ok("ok");
        }

        [HttpPost("DeviceGroup")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult DeviceGroup([FromBody] DeviceGroup deviceGroup)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            var result = DataService.AddDeviceGroup(orgID, deviceGroup, out var deviceGroupID, out var errorMessage);
            if (!result)
            {
                return BadRequest(errorMessage);
            }
            return Ok(deviceGroupID);
        }

        [HttpDelete("DeviceGroup/{groupID}/Users/")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public async Task<IActionResult> DeviceGroupRemoveUser([FromBody] string userID, string groupID)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            if (!await DataService.RemoveUserFromDeviceGroup(orgID, groupID, userID))
            {
                return BadRequest("Bład podczas usuwania użytkownika z grupy.");
            }
            return Ok();
        }

        [HttpPost("DeviceGroup/{groupID}/Users/")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult DeviceGroupAddUser([FromBody] string userID, string groupID)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            var result = DataService.AddUserToDeviceGroup(orgID, groupID, userID, out var resultMessage);
            if (!result)
            {
                return BadRequest(resultMessage);
            }

            return Ok(resultMessage);
        }

        [HttpGet("GenerateResetUrl/{userID}")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public async Task<IActionResult> GenerateResetUrl(string userID)
        {
            if (User.Identity.IsAuthenticated &&
              !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);

            var user = await UserManager.FindByIdAsync(userID);

            if (user.OrganizationID != orgID)
            {
                return Unauthorized();
            }

            var code = await UserManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code },
                protocol: Request.Scheme);

            return Ok(callbackUrl);

        }

        [HttpPut("Name")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult Name([FromBody] string organizationName)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }
            if (organizationName.Length > 25)
            {
                return BadRequest();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            DataService.UpdateOrganizationName(orgID, organizationName.Trim());
            return Ok("ok");
        }

        [HttpPut("SetDefault")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public IActionResult SetDefault([FromBody] bool isDefault)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsServerAdmin)
            {
                return Unauthorized();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);
            DataService.SetIsDefaultOrganization(orgID, isDefault);
            return Ok("ok");
        }

        [HttpPost("SendInvite")]
        [ServiceFilter(typeof(ApiAuthorizationFilter))]
        public async Task<IActionResult> SendInvite([FromBody] InviteViewModel invite)
        {
            if (User.Identity.IsAuthenticated &&
                !DataService.GetUserByNameWithOrg(User.Identity.Name).IsAdministrator)
            {
                return Unauthorized();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            Request.Headers.TryGetValue("OrganizationID", out var orgID);


            if (!DataService.DoesUserExist(invite.InvitedUser))
            {
                var result = await DataService.CreateUser(invite.InvitedUser, invite.IsAdmin, orgID);
                if (result)
                {
                    var user = await UserManager.FindByEmailAsync(invite.InvitedUser);

                    await UserManager.ConfirmEmailAsync(user, await UserManager.GenerateEmailConfirmationTokenAsync(user));

                    return Ok();
                }
                else
                {
                    return BadRequest("Wystąpił problem z utworzeniem nowego konta.");
                }
            }
            else
            {
                var newInvite = DataService.AddInvite(orgID, invite);

                var inviteURL = $"{Request.Scheme}://{Request.Host}/Invite?id={newInvite.ID}";
                var emailResult = await EmailSender.SendEmailAsync(invite.InvitedUser, "Zaproszenie do nex-Remote",
                            $@"<img src='{Request.Scheme}://{Request.Host}/images/nex-Remote_Logo.png'/>
                            <br><br>
                            Witaj!
                            <br><br>
                            Zostałeś zaproszony do systemu pomocy zdalnej nex-Remote by nex-IT Jakub Potoczny.
                            <br><br>
                            Możesz dołaczyć poprzez <a href='{HtmlEncoder.Default.Encode(inviteURL)}'>kliknij tutaj</a>.",
                            orgID);

                if (!emailResult)
                {
                    return Problem("Wystąpił problem z wysłaniem zaproszenia.");
                }

                return Ok();
            }

        }
    }
}
