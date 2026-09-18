using System;
using System.Runtime.InteropServices;
using System.Text;
using ColossalFramework.PlatformServices;
using ColossalFramework.Threading;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Minimal Steam Friends rich-presence bridge. The callback ABI follows the MIT-licensed
    /// Steamworks.NET callback dispatcher used by CitiesSkylinesMultiplayer/CSM; see THIRD-PARTY-NOTICES.txt.
    /// Steam is discovery/presentation only. Forge MemberIdentity remains the network identity.
    /// </summary>
    internal static class ForgeSteamRichPresence
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct GameRichPresenceJoinRequested
        {
            public ulong Friend;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Connect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class CallbackBase
        {
            public IntPtr VTable;
            public byte Flags;
            public int Callback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class CallbackVTable
        {
            [MarshalAs(UnmanagedType.FunctionPtr)] public RunCallback Run;
            [MarshalAs(UnmanagedType.FunctionPtr)] public RunCallResult RunResult;
            [MarshalAs(UnmanagedType.FunctionPtr)] public GetSize GetCallbackSize;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RunCallback(IntPtr self, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RunCallResult(
            IntPtr self, IntPtr value, [MarshalAs(UnmanagedType.I1)] bool failed, ulong call);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSize(IntPtr self);

        private const int JoinRequestedCallback = 337;
        private static IntPtr friends;
        private static IntPtr vtableMemory;
        private static GCHandle callbackHandle;
        private static CallbackVTable callbackVTable;
        private static CallbackBase callbackBase;
        private static bool use64 = true;
        private static bool initialized;
        private static bool commandLineChecked;
        private static bool platformShutdownSubscribed;

        internal static void Initialize()
        {
            if (!commandLineChecked) { commandLineChecked = true; CheckCommandLine(); }
            if (initialized || !PlatformService.active) return;
            try
            {
                if (!platformShutdownSubscribed)
                {
                    PlatformService.eventPlatformServiceShutdown += OnPlatformServiceShutdown;
                    platformShutdownSubscribed = true;
                }
                int user = GetHSteamUser(); int pipe = GetHSteamPipe(); IntPtr client = GetSteamClient();
                if (user == 0 || pipe == 0 || client == IntPtr.Zero) return;
                friends = GetFriends(client, user, pipe, "SteamFriends015");
                if (friends == IntPtr.Zero) return;
                callbackVTable = new CallbackVTable
                { Run = OnRun, RunResult = OnRunResult, GetCallbackSize = OnGetSize };
                vtableMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CallbackVTable)));
                Marshal.StructureToPtr(callbackVTable, vtableMemory, false);
                callbackBase = new CallbackBase { VTable = vtableMemory, Flags = 0, Callback = JoinRequestedCallback };
                callbackHandle = GCHandle.Alloc(callbackBase, GCHandleType.Pinned);
                RegisterCallback(callbackHandle.AddrOfPinnedObject(), JoinRequestedCallback);
                initialized = true;
            }
            catch { Shutdown(); }
        }

        internal static bool PublishInvite(string invite, int playerCount)
        {
            Initialize();
            if (!initialized || string.IsNullOrEmpty(invite)) return false;
            string connect = "forge=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(invite));
            if (Encoding.UTF8.GetByteCount(connect) >= 256) return false;
            try
            {
                SetPresence("status", "Playing CSM-Forge multiplayer");
                SetPresence("connect", connect);
                SetPresence("steam_player_group", connect.Length > 64 ? connect.Substring(0, 64) : connect);
                SetPresence("steam_player_group_size", Math.Max(1, playerCount).ToString());
                PlatformService.SetRichPresence("Playing CSM-Forge multiplayer");
                PlatformService.SetRichPresenceVisibility(true);
                return true;
            }
            catch { return false; }
        }

        internal static void SetPlayerCount(int count)
        {
            if (!initialized) return;
            try { SetPresence("steam_player_group_size", Math.Max(1, count).ToString()); } catch { }
        }

        internal static void Clear()
        {
            if (friends != IntPtr.Zero && PlatformService.active) try { ClearPresence(); } catch { }
        }

        internal static void Shutdown()
        {
            if (platformShutdownSubscribed)
            {
                PlatformService.eventPlatformServiceShutdown -= OnPlatformServiceShutdown;
                platformShutdownSubscribed = false;
            }
            bool nativeAvailable = PlatformService.active;
            if (nativeAvailable) Clear();
            ReleaseCallback(nativeAvailable);
        }

        private static void OnPlatformServiceShutdown()
        {
            ReleaseCallback(true);
        }

        private static void ReleaseCallback(bool unregisterNative)
        {
            if (callbackHandle.IsAllocated)
            {
                if (unregisterNative && friends != IntPtr.Zero)
                    try { UnregisterCallback(callbackHandle.AddrOfPinnedObject()); } catch { }
                callbackHandle.Free();
            }
            if (vtableMemory != IntPtr.Zero) { Marshal.FreeHGlobal(vtableMemory); vtableMemory = IntPtr.Zero; }
            callbackBase = null; callbackVTable = null; friends = IntPtr.Zero; initialized = false;
        }

        private static void CheckCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("forge=", StringComparison.Ordinal)) DispatchConnect(args[i]);
        }

        private static void OnRun(IntPtr self, IntPtr value)
        {
            try
            {
                GameRichPresenceJoinRequested request =
                    (GameRichPresenceJoinRequested)Marshal.PtrToStructure(value, typeof(GameRichPresenceJoinRequested));
                DispatchConnect(ReadUtf8(request.Connect));
            }
            catch { }
        }

        private static void OnRunResult(IntPtr self, IntPtr value, bool failed, ulong call) { OnRun(self, value); }
        private static int OnGetSize(IntPtr self) { return Marshal.SizeOf(typeof(GameRichPresenceJoinRequested)); }

        private static void DispatchConnect(string connect)
        {
            if (string.IsNullOrEmpty(connect) || !connect.StartsWith("forge=", StringComparison.Ordinal)) return;
            string invite;
            try { invite = Encoding.UTF8.GetString(Convert.FromBase64String(connect.Substring(6))); }
            catch { return; }
            ThreadHelper.dispatcher.Dispatch(delegate { ForgeMultiplayerUi.AcceptSteamInvite(invite); });
        }

        private static string ReadUtf8(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            int length = 0; while (length < bytes.Length && bytes[length] != 0) length++;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        private static int GetHSteamUser()
        {
            try { return SteamAPI_GetHSteamUser64(); }
            catch (DllNotFoundException) { use64 = false; return SteamAPI_GetHSteamUser32(); }
        }
        private static int GetHSteamPipe() { return use64 ? SteamAPI_GetHSteamPipe64() : SteamAPI_GetHSteamPipe32(); }
        private static IntPtr GetSteamClient() { return use64 ? SteamClient64() : SteamClient32(); }
        private static IntPtr GetFriends(IntPtr client, int user, int pipe, string version)
        { return use64 ? GetFriends64(client, user, pipe, version) : GetFriends32(client, user, pipe, version); }
        private static bool SetPresence(string key, string value)
        { return use64 ? SetPresence64(friends, key, value) : SetPresence32(friends, key, value); }
        private static void ClearPresence() { if (use64) ClearPresence64(friends); else ClearPresence32(friends); }
        private static void RegisterCallback(IntPtr callback, int identity)
        { if (use64) RegisterCallback64(callback, identity); else RegisterCallback32(callback, identity); }
        private static void UnregisterCallback(IntPtr callback)
        { if (use64) UnregisterCallback64(callback); else UnregisterCallback32(callback); }

        [DllImport("steam_api64", EntryPoint = "SteamAPI_GetHSteamUser", CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_GetHSteamUser64();
        [DllImport("steam_api64", EntryPoint = "SteamAPI_GetHSteamPipe", CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_GetHSteamPipe64();
        [DllImport("steam_api64", EntryPoint = "SteamClient", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamClient64();
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamClient_GetISteamFriends", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GetFriends64(IntPtr client, int user, int pipe, [MarshalAs(UnmanagedType.LPStr)] string version);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamFriends_SetRichPresence", CallingConvention = CallingConvention.Cdecl)] private static extern bool SetPresence64(IntPtr instance, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamFriends_ClearRichPresence", CallingConvention = CallingConvention.Cdecl)] private static extern void ClearPresence64(IntPtr instance);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_RegisterCallback", CallingConvention = CallingConvention.Cdecl)] private static extern void RegisterCallback64(IntPtr callback, int identity);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_UnregisterCallback", CallingConvention = CallingConvention.Cdecl)] private static extern void UnregisterCallback64(IntPtr callback);

        [DllImport("steam_api", EntryPoint = "SteamAPI_GetHSteamUser", CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_GetHSteamUser32();
        [DllImport("steam_api", EntryPoint = "SteamAPI_GetHSteamPipe", CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_GetHSteamPipe32();
        [DllImport("steam_api", EntryPoint = "SteamClient", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamClient32();
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamClient_GetISteamFriends", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GetFriends32(IntPtr client, int user, int pipe, [MarshalAs(UnmanagedType.LPStr)] string version);
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamFriends_SetRichPresence", CallingConvention = CallingConvention.Cdecl)] private static extern bool SetPresence32(IntPtr instance, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamFriends_ClearRichPresence", CallingConvention = CallingConvention.Cdecl)] private static extern void ClearPresence32(IntPtr instance);
        [DllImport("steam_api", EntryPoint = "SteamAPI_RegisterCallback", CallingConvention = CallingConvention.Cdecl)] private static extern void RegisterCallback32(IntPtr callback, int identity);
        [DllImport("steam_api", EntryPoint = "SteamAPI_UnregisterCallback", CallingConvention = CallingConvention.Cdecl)] private static extern void UnregisterCallback32(IntPtr callback);
    }
}
