using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using nexRemote.Server.Components;
using nexRemote.Server.Components.ModalContents;
using nexRemote.Server.Services;
using nexRemote.Shared.Models;
using nexRemote.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace nexRemote.Server.Pages
{
    public partial class ManageOrganization : AuthComponentBase
    {
        private readonly List<DeviceGroup> _deviceGroups = new();
        private readonly List<InviteLink> _invites = new();
        private readonly List<nexRemoteUser> _orgUsers = new();
        private bool _inviteAsAdmin;
        private string _inviteEmail;
        private string _newDeviceGroupName;
        private Organization _organization;
        private string _selectedDeviceGroupId;

        [Inject]
        private IDataService DataService { get; set; }

        [Inject]
        private IEmailSenderEx EmailSender { get; set; }

        [Inject]
        private IJsInterop JsInterop { get; set; }

        [Inject]
        private IModalService ModalService { get; set; }

        [Inject]
        private NavigationManager NavManager { get; set; }

        [Inject]
        private IToastService ToastService { get; set; }
        [Inject]
        private UserManager<nexRemoteUser> UserManager { get; set; }


        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            await RefreshData();
        }

        private void CreateNewDeviceGroup()
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_newDeviceGroupName))
            {
                return;
            }

            var deviceGroup = new DeviceGroup()
            {
                Name = _newDeviceGroupName
            };

            var result = DataService.AddDeviceGroup(User.OrganizationID, deviceGroup, out _, out var errorMessage);
            if (!result)
            {
                ToastService.ShowToast(errorMessage, classString: "bg-danger");
                return;
            }

            ToastService.ShowToast("Utworzono grupę urządzeń.");
            _deviceGroups.Add(deviceGroup);
            _newDeviceGroupName = string.Empty;
        }

        private void DefaultOrgCheckChanged(ChangeEventArgs args)
        {
            if (!User.IsServerAdmin)
            {
                return;
            }

            var isDefault = (bool)args.Value;
            DataService.SetIsDefaultOrganization(_organization.ID, isDefault);
            ToastService.ShowToast("Domyślna organizacja ustawiona.");
        }

        private async Task DeleteInvite(InviteLink invite)
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            var result = await JsInterop.Confirm("Chcesz usunąć to zaproszenie?");
            if (!result)
            {
                return;
            }

            DataService.DeleteInvite(User.OrganizationID, invite.ID);
            _invites.RemoveAll(x => x.ID == invite.ID);
            ToastService.ShowToast("Zaproszenie usunięte.");
        }

        private async Task DeleteSelectedDeviceGroup()
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedDeviceGroupId))
            {
                return;
            }

            var result = await JsInterop.Confirm("Chcesz usunąć grupę urządzeń?");
            if (!result)
            {
                return;
            }

            DataService.DeleteDeviceGroup(User.OrganizationID, _selectedDeviceGroupId);
            _deviceGroups.RemoveAll(x => x.ID == _selectedDeviceGroupId);
            _selectedDeviceGroupId = string.Empty;
        }

        private async Task DeleteUser(nexRemoteUser user)
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            if (User.Id == user.Id)
            {
                ToastService.ShowToast("Nie można usunąć siebie samego.", classString: "bg-warning");
                return;
            }

            var result = await JsInterop.Confirm("Chcesz usunąć tego użytkownika?");
            if (!result)
            {
                return;
            }

            await DataService.DeleteUser(User.OrganizationID, user.Id);
            _orgUsers.RemoveAll(x => x.Id == user.Id);
            ToastService.ShowToast("Użytkownik usunięty.");
        }

        private async Task EditDeviceGroups(nexRemoteUser user)
        {
            void editDeviceGroupsModal(RenderTreeBuilder builder)
            {
                var deviceGroups = DataService.GetDeviceGroupsForOrganization(user.OrganizationID);

                builder.OpenComponent<EditDeviceGroup>(0);
                builder.AddAttribute(1, EditDeviceGroup.EditUserPropName, user);
                builder.AddAttribute(2, EditDeviceGroup.DeviceGroupsPropName, deviceGroups);
                builder.CloseComponent();
            }
            await ModalService.ShowModal("Grupy urządzeń", editDeviceGroupsModal);
        }

        private async Task EvaluateInviteInputKeypress(KeyboardEventArgs args)
        {
            if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
            {
                await SendInvite();
            }
        }

        private void EvaluateNewDeviceGroupKeyPress(KeyboardEventArgs args)
        {
            if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
            {
                CreateNewDeviceGroup();
            }
        }
        private void OrganizationNameChanged(ChangeEventArgs args)
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            var newName = (string)args.Value;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            if (newName.Length > 25)
            {
                ToastService.ShowToast("Max 25 znaków.",
                    classString: "bg-warning");
                return;
            }

            DataService.UpdateOrganizationName(_organization.ID, newName);
            _organization.OrganizationName = newName;
            ToastService.ShowToast("Nazwa organizacji zmieniona.");
        }

        private async Task RefreshData()
        {
            _organization = await DataService.GetOrganizationByUserName(Username);

            _orgUsers.Clear();
            _invites.Clear();
            _deviceGroups.Clear();

            _invites.AddRange(DataService.GetAllInviteLinks(User.OrganizationID).OrderBy(x => x.InvitedUser));
            _deviceGroups.AddRange(DataService.GetDeviceGroups(Username).OrderBy(x => x.Name));
            var orgUsers = await DataService.GetAllUsersInOrganization(User.OrganizationID);
            _orgUsers.AddRange(orgUsers.OrderBy(x => x.UserName));
        }
        private async Task ResetPassword(nexRemoteUser user)
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            var code = await UserManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var resetUrl = $"{NavManager.BaseUri}Identity/Account/ResetPassword?code={code}";

            await ModalService.ShowModal("Reset Hasła", builder =>
            {
                builder.AddMarkupContent(0, $@"<div class=""mb-3"">Reset Hasła URL:</div>
                    <input readonly value=""{resetUrl}"" class=""form-control"" /> 
                    <div class=""mt-3"">UWAGA: Podaj ten adres URL użytkownikowi. Muszą być całkowicie wylogowani, aby to zadziałało.</div>");
            });
        }

        private async Task SendInvite()
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            if (!DataService.DoesUserExist(_inviteEmail))
            {
                var result = await DataService.CreateUser(_inviteEmail, _inviteAsAdmin, User.OrganizationID);
                if (result)
                {
                    var user = await DataService.GetUserAsync(_inviteEmail);

                    await UserManager.ConfirmEmailAsync(user, await UserManager.GenerateEmailConfirmationTokenAsync(user));

                    _orgUsers.Add(user);

                    _inviteAsAdmin = false;
                    _inviteEmail = string.Empty;
                    ToastService.ShowToast("Utworzono konto użytkownika.");
                    return;
                }
                else
                {
                    ToastService.ShowToast("Nie udało się utworzyć użytkownika.", classString: "bg-danger");
                    return;
                }
            }
            else
            {

                var invite = new InviteViewModel()
                {
                    InvitedUser = _inviteEmail,
                    IsAdmin = _inviteAsAdmin
                };
                var newInvite = DataService.AddInvite(User.OrganizationID, invite);

                var inviteURL = $"{NavManager.BaseUri}Invite?id={newInvite.ID}";
                var emailResult = await EmailSender.SendEmailAsync(invite.InvitedUser, "Zaproszenie do organizacji w nexRemote",
                        $@"<img src='{NavManager.BaseUri}images/nexRemote_Logo.png'/>
                            <br><br>
                            Witaj!
                            <br><br>
                            Zaproszono Cię do dołączenia do organizacji w nexRemote.
                            <br><br>
                            Możesz dołączyć do organizacji przez <a href='{HtmlEncoder.Default.Encode(inviteURL)}'>clicking here</a>.",
                        User.OrganizationID);
                if (emailResult)
                {
                    ToastService.ShowToast("Zaproszenie wysłane.");
                    
                    _inviteAsAdmin = false;
                    _inviteEmail = string.Empty;
                    _invites.Add(newInvite);
                }
                else
                {
                    ToastService.ShowToast("Błąd podczas wysyłania e-maila z zaproszeniem.", classString: "bg-danger");
                }
            }
        }

        private void SetUserIsAdmin(ChangeEventArgs args, nexRemoteUser orgUser)
        {
            if (!User.IsAdministrator)
            {
                return;
            }

            var isAdmin = (bool)args.Value;
            DataService.ChangeUserIsAdmin(User.OrganizationID, orgUser.Id, isAdmin);
            ToastService.ShowToast("Ustawiona wartość administratora.");
        }

        private void ShowDefaultOrgHelp()
        {
            ModalService.ShowModal("Domyślna organizacja", new[]
            {
                @"Ta opcja jest dostępna tylko dla administratorów serwera. Po wybraniu
                ustawia tę organizację jako domyślną dla serwera. Jeśli organizacja nie może
                zostaną określone w aplikacjach szybkiej pomocy, będą używać domyślnego brandingu organizacji."
            });
        }

        private void ShowDeviceGroupHelp()
        {
            ModalService.ShowModal("Grupy urządzeń", new[]
           {
                "Grupy urządzeń mogą być używane do ograniczania uprawnień użytkowników i filtrowania komputerów na " +
                "stronie głównej.",
                "Każdy będzie miał dostęp do urządzeń spoza grupy. Tylko " +
                "administratorzy i użytkownicy w grupie urządzeń będą mieli dostęp do urządzeń w tej grupie."
            });
        }

        private void ShowInvitesHelp()
        {
            ModalService.ShowModal("Zaproszenia", new[]
           {
                "Wszystkie oczekujące zaproszenia będą wyświetlane tutaj i można je odwołać, usuwając je.",

                "Jeśli użytkownik nie istnieje, wysłanie zaproszenia spowoduje utworzenie jego konta i dodanie go do obecnej organizacji. " +
                "Adres URL resetowania hasła można wygenerować z tabeli użytkowników.",

                "Pole wyboru Administrator określa, czy nowy użytkownik będzie miał uprawnienia administratora w tej organizacji."
            });
        }

        private void ShowRelayCodeHelp()
        {
            ModalService.ShowModal("Kod", new[]
            {
                @"Ten kod zostanie dołączony do nazw plików EXE. Gdyby nexRemote został skompilowany
                ze źródła i miał osadzony adres URL serwera, użyj tego kodu do zidentyfikowania organizacji."
            });
        }
        private void ShowUsersHelp()
        {
            ModalService.ShowModal("Użytkownicy", new[]
            {
                "Tutaj zarządza się wszystkimi użytkownikami organizacji",
                "Administratorzy będą mieli dostęp do tego ekranu zarządzania, a także do wszystkich komputerów."
            });
        }
    }
}
