// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam;

// The results of Steam's protocol that the state machines act on, with the numbers of SteamKit2's
// EResult, so that the adapters convert with a cast and nothing outside them sees a SteamKit2
// type (steam-service.md section 3). Any other number keeps its value and is named by it.
internal enum SteamResult
{
    Invalid = 0,
    OK = 1,
    Fail = 2,
    NoConnection = 3,
    InvalidPassword = 5,
    LoggedInElsewhere = 6,
    InvalidParam = 8,
    FileNotFound = 9,
    Busy = 10,
    InvalidState = 11,
    AccessDenied = 15,
    Timeout = 16,
    Banned = 17,
    AccountNotFound = 18,
    ServiceUnavailable = 20,
    NotLoggedOn = 21,
    Pending = 22,
    LimitExceeded = 25,
    Revoked = 26,
    Expired = 27,
    ConnectFailed = 35,
    RemoteDisconnect = 38,
    AccountDisabled = 43,
    TryAnotherCM = 48,
    Suspended = 51,
    Cancelled = 52,
    AccountLogonDenied = 63,
    InvalidLoginAuthCode = 65,
    AccountLockedDown = 73,
    RateLimitExceeded = 84,
    AccountLoginDeniedNeedTwoFactor = 85,
    AccountLoginDeniedThrottle = 87,
    TwoFactorCodeMismatch = 88,
    InvalidSignature = 121,
}
