﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LC.Newtonsoft.Json;
using TapTap.Common;
using TapTap.Common.Internal.Utils;
using TapTap.Login.Internal;
using TapTap.Login.Internal.Http;
using UnityEngine;

namespace TapTap.Login.Standalone {
    public class TapLoginStandalone : ITapLoginPlatform {

        private static bool isOverseas = false;
        internal static bool IsOverseas => isOverseas;
        public void Init(string clientID) {
        }

        public void Init(string clientID, bool isCn, bool roundCorner) {
            isOverseas = !isCn;
        }

        public void ChangeConfig(bool roundCorner, bool isPortrait) {
        }

        public Task<Profile> FetchProfile() {
            return LoginHelper.GetProfile();
        }

        public Task<Profile> GetProfile() {
            return LoginHelper.GetProfile();
        }

        public Task<AccessToken> GetAccessToken() {
            return LoginHelper.GetAccessToken();
        }

        public Task<AccessToken> Authorize(string[] permissions = null) {
            List<string> allPermissions = new List<string>(TapLogin.DefaultPermissions);
            if (permissions != null) {
                allPermissions.AddRange(permissions);
            }
            if (TapCommon.Config.RegionType == RegionType.IO) {
                allPermissions.Remove(TapLogin.TAP_LOGIN_SCOPE_COMPLIANCE);
            }
            return AuthorizeInternal(allPermissions);
        }

        public Task<AccessToken> Login() {
            return Login(new string[] {});
        }

        public async Task<AccessToken> Login(string[] permissions) {
            List<string> allPermissions = new List<string>(permissions);
            if (permissions == null || permissions.Length == 0) {
                allPermissions.Add(TapLogin.TAP_LOGIN_SCOPE_PUBLIC_PROFILE);
            }
            if (TapCommon.Config.RegionType == RegionType.IO) {
                allPermissions.Remove(TapLogin.TAP_LOGIN_SCOPE_COMPLIANCE);
            }
            allPermissions.AddRange(TapLogin.DefaultPermissions);
            AccessToken token = await AuthorizeInternal(allPermissions);

            try {
                ProfileData profileData = await LoginService.GetProfile(TapTapSdk.ClientId, token);
                Profile profile = ConvertToProfile(profileData);

                SaveTapUser(token, profile);

                string message = string.Format(LoginLanguage.GetCurrentLang().LoginNotice(), profile.name);
                Texture avatar = await ImageUtils.LoadImage(profile.avatar);
                UI.UIManager.Instance.OpenToast(false, message, icon: avatar);
            } catch (TapException e) {
                throw e;
            } catch (Exception) {
                throw new TapException((int) TapErrorCode.ERROR_CODE_UNDEFINED, "UnKnow Error");
            }

            return token;
        }

        public async Task<AccessToken> Login(TapLoginPermissionConfig config) { 
            Tuple<AccessToken, Profile> result = await AuthorizeInternal(config);
            SaveTapUser(result.Item1, result.Item2);
            return result.Item1;
        }

        public void Logout() {
            LoginHelper.Logout();
        }

        public Task<bool> GetTestQualification() {
            return Task.FromResult(false);
        }

        private Task<AccessToken> AuthorizeInternal(IEnumerable<string> permissions) {
            TaskCompletionSource<AccessToken> tcs = new TaskCompletionSource<AccessToken>();
            LoginPanelController.OpenParams openParams = new LoginPanelController.OpenParams {
                ClientId = TapTapSdk.ClientId,
                Permissions = new HashSet<string>(permissions).ToArray(),
                OnAuth = tokenData => {
                    if (tokenData == null) {
                        tcs.TrySetException(new TapException((int) TapErrorCode.ERROR_CODE_UNDEFINED, "UnKnow Error"));
                    } else {
                        // 将 TokenData 转化为 AccessToken
                        AccessToken accessToken = ConvertToAccessToken(tokenData);
                        tcs.TrySetResult(accessToken);
                    }
                },
                OnError = e => {
                    tcs.TrySetException(e);
                },
                OnClose = () => {
                    tcs.TrySetException(
                        new TapException((int) TapErrorCode.ERROR_CODE_LOGIN_CANCEL, "Login Cancel"));
                }
            };
            TapTap.UI.UIManager.Instance.OpenUI<LoginPanelController>("Prefabs/TapLogin/LoginPanel", openParams);
            return tcs.Task;
        }

        private Task<Tuple<AccessToken, Profile>> AuthorizeInternal(TapLoginPermissionConfig config) {
            TaskCompletionSource<Tuple<AccessToken, Profile>> tcs = new TaskCompletionSource<Tuple<AccessToken, Profile>>();
            LoginWithPermissionsPanelController.OpenParams openParams = new LoginWithPermissionsPanelController.OpenParams {
                ClientId = TapTapSdk.ClientId,
                Name = config.Name,
                Permissions = config.Permissions,
                OnAuth = (tokenData, profileData) => {
                    // 将 TokenData 转化为 AccessToken
                    AccessToken accessToken = ConvertToAccessToken(tokenData);
                    Profile profile = ConvertToProfile(profileData);
                    tcs.TrySetResult(new Tuple<AccessToken, Profile>(accessToken, profile));
                }
            };
            TapTap.UI.UIManager.Instance.OpenUI<LoginWithPermissionsPanelController>("Prefabs/TapLogin/LoginWithPermissionPanel", openParams);
            return tcs.Task;
        }

        private static AccessToken ConvertToAccessToken(TokenData tokenData) {
            return new AccessToken {
                kid = tokenData.Kid,
                accessToken = tokenData.Token,
                tokenType = tokenData.TokenType,
                macKey = tokenData.MacKey,
                macAlgorithm = tokenData.MacAlgorithm,
                scopeSet = tokenData.Scopes
            };
        }

        private static Profile ConvertToProfile(ProfileData profileData) {
            return new Profile {
                openid = profileData.OpenId,
                unionid = profileData.UnionId,
                name = profileData.Name,
                avatar = profileData.Avatar,
                gender = profileData.Gender
            };
        }

        private static void SaveTapUser(AccessToken accessToken, Profile profile) {
            DataStorage.SaveString("taptapsdk_accesstoken", JsonConvert.SerializeObject(accessToken));
            DataStorage.SaveString("taptapsdk_profile", JsonConvert.SerializeObject(profile));
        }
    }
}
