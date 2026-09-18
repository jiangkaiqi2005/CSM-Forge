using System;
using System.Runtime.InteropServices;
using System.Text;
using ColossalFramework.PlatformServices;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Steam Rich Presence discovery adapter, source-grounded in CSM's SteamHelpers and its
    /// vendored Steamworks.NET callback dispatcher. Native calls are confined to Pump(), which
    /// ForgeMenuPump invokes on Unity's UI thread. Simulation/network callers only publish
    /// immutable desired state under the managed lock.
    ///
    /// Steam is discovery/presentation only. The connect value contains a bounded Forge invite;
    /// Forge MemberIdentity remains the network identity and no Steam/native ID enters the wire model.
    /// </summary>
    internal static class ForgeSteamRichPresence
    {
        private const int JoinRequestedCallback = 337;
        private const int MaxConnectBytes = 255;
        private const string FriendsVersion = "SteamFriends015";
        private static readonly object Gate = new object();

        private static IntPtr friends;
        private static IntPtr vtableMemory;
        private static GCHandle callbackHandle;
        private static CallbackVTable callbackVTable;
        private static CallbackBase callbackBase;
        private static bool use64 = true;
        private static volatile bool initialized;
        private static bool initializationAttempted;
        private static bool commandLineChecked;
        private static bool platformShutdownSubscribed;
        private static bool stateDirty;
        private static string desiredConnect;
        private static int desiredPlayerCount = 1;
        private static string pendingJoinInvite;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct GameRichPresenceJoinRequested
        {
            public ulong FriendSteamId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Connect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class CallbackBase
        {
            public IntPtr VTable;
            public byte Flags;
            public int Callback;
        }

        // CS1's Mono Steam callback surface uses the same Cdecl/non-MSVC layout that CSM's
        // Steamworks.NET-derived dispatcher selects for its shipped runtime.
        [StructLayout(LayoutKind.Sequential)]
        private sealed class CallbackVTable
        {
            [MarshalAs(UnmanagedType.FunctionPtr)] public RunCallback Run;
            [MarshalAs(UnmanagedType.FunctionPtr)] public RunCallResult RunResult;
            [MarshalAs(UnmanagedType.FunctionPtr)] public GetSize GetCallbackSize;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RunCallback(IntPtr self, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RunCallResult(IntPtr self, IntPtr value,
            [MarshalAs(UnmanagedType.I1)] bool failed, ulong call);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetSize(IntPtr self);

        internal static bool AutomaticJoinAvailable { get { return initialized; } }

        internal static bool PublishInvite(string invite, int playerCount)
        {
            if (string.IsNullOrEmpty(invite)) return false;
            string connect = "forge=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(invite));
            if (Encoding.UTF8.GetByteCount(connect) > MaxConnectBytes) return false;
            lock (Gate)
            {
                desiredConnect = connect;
                desiredPlayerCount = Math.Max(1, playerCount);
                stateDirty = true;
            }
            return initialized;
        }

        internal static void SetPlayerCount(int playerCount)
        {
            lock (Gate)
            {
                desiredPlayerCount = Math.Max(1, playerCount);
                if (!string.IsNullOrEmpty(desiredConnect)) stateDirty = true;
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                desiredConnect = null;
                desiredPlayerCount = 1;
                stateDirty = true;
            }
        }

        /// <summary>Called only by ForgeMenuPump.Update on Unity's UI thread.</summary>
        internal static void Pump()
        {
            CheckCommandLine();
            if (!initialized && !initializationAttempted && PlatformService.active) InitializeOnMainThread();

            string invite;
            lock (Gate) { invite = pendingJoinInvite; pendingJoinInvite = null; }
            if (!string.IsNullOrEmpty(invite)) ForgeMultiplayerUi.AcceptSteamInvite(invite);

            if (!initialized) return;
            string connect;
            int playerCount;
            lock (Gate)
            {
                if (!stateDirty) return;
                connect = desiredConnect;
                playerCount = desiredPlayerCount;
                stateDirty = false;
            }
            try
            {
                if (string.IsNullOrEmpty(connect))
                {
                    ClearRichPresence();
                    return;
                }
                SetRichPresence("status", "Playing CSM-Forge multiplayer");
                SetRichPresence("connect", connect);
                SetRichPresence("steam_player_group", GroupToken(connect));
                SetRichPresence("steam_player_group_size", playerCount.ToString());
                PlatformService.SetRichPresence("Playing CSM-Forge multiplayer");
                PlatformService.SetRichPresenceVisibility(true);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[CSM-Forge] Steam Rich Presence update failed: " + error);
            }
        }

        internal static void Shutdown()
        {
            if (platformShutdownSubscribed)
            {
                PlatformService.eventPlatformServiceShutdown -= OnPlatformServiceShutdown;
                platformShutdownSubscribed = false;
            }
            bool nativeAvailable = PlatformService.active;
            if (nativeAvailable && initialized)
            {
                try { ClearRichPresence(); } catch { }
            }
            ReleaseCallback(nativeAvailable);
            lock (Gate)
            {
                desiredConnect = null;
                pendingJoinInvite = null;
                desiredPlayerCount = 1;
                stateDirty = false;
            }
            initializationAttempted = false;
            commandLineChecked = false;
        }

        private static void InitializeOnMainThread()
        {
            initializationAttempted = true;
            try
            {
                // CSM initializes Steam before asking for its user/pipe/interface handles.
                // The game owns process shutdown; this adapter deliberately omits SteamAPI_Shutdown.
                if (!SteamApiInit()) return;
                int user = GetHSteamUser();
                int pipe = GetHSteamPipe();
                IntPtr client = GetSteamClient();
                if (user == 0 || pipe == 0 || client == IntPtr.Zero) return;
                friends = GetFriends(client, user, pipe, FriendsVersion);
                if (friends == IntPtr.Zero) return;

                callbackVTable = new CallbackVTable
                {
                    Run = OnRun,
                    RunResult = OnRunResult,
                    GetCallbackSize = OnGetSize
                };
                vtableMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CallbackVTable)));
                Marshal.StructureToPtr(callbackVTable, vtableMemory, false);
                callbackBase = new CallbackBase
                {
                    VTable = vtableMemory,
                    Flags = 0,
                    Callback = JoinRequestedCallback
                };
                callbackHandle = GCHandle.Alloc(callbackBase, GCHandleType.Pinned);
                RegisterCallback(callbackHandle.AddrOfPinnedObject(), JoinRequestedCallback);
                PlatformService.eventPlatformServiceShutdown += OnPlatformServiceShutdown;
                platformShutdownSubscribed = true;
                initialized = true;
                lock (Gate) stateDirty = true;
                UnityEngine.Debug.Log("[CSM-Forge] Steam Rich Presence bridge initialized on UI thread.");
            }
            catch (Exception error)
            {
                ReleaseCallback(PlatformService.active);
                UnityEngine.Debug.LogWarning("[CSM-Forge] Steam Rich Presence unavailable: " + error.GetType().Name);
            }
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
            if (vtableMemory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vtableMemory);
                vtableMemory = IntPtr.Zero;
            }
            callbackBase = null;
            callbackVTable = null;
            friends = IntPtr.Zero;
            initialized = false;
        }

        private static void OnRun(IntPtr self, IntPtr value)
        {
            try
            {
                GameRichPresenceJoinRequested request =
                    (GameRichPresenceJoinRequested)Marshal.PtrToStructure(value, typeof(GameRichPresenceJoinRequested));
                QueueConnect(ReadUtf8(request.Connect));
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[CSM-Forge] Steam join callback failed: " + error);
            }
        }

        private static void OnRunResult(IntPtr self, IntPtr value, bool failed, ulong call)
        {
            if (!failed) OnRun(self, value);
        }

        private static int OnGetSize(IntPtr self)
        {
            return Marshal.SizeOf(typeof(GameRichPresenceJoinRequested));
        }

        private static void QueueConnect(string connect)
        {
            if (string.IsNullOrEmpty(connect) || !connect.StartsWith("forge=", StringComparison.Ordinal)) return;
            string invite;
            try { invite = Encoding.UTF8.GetString(Convert.FromBase64String(connect.Substring(6))); }
            catch { return; }
            lock (Gate) pendingJoinInvite = invite;
        }

        private static void CheckCommandLine()
        {
            if (commandLineChecked) return;
            commandLineChecked = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("forge=", StringComparison.Ordinal)) QueueConnect(args[i]);
        }

        private static string GroupToken(string connect)
        {
            byte[] digest;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(connect));
            return Convert.ToBase64String(digest);
        }

        private static string ReadUtf8(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            int length = 0;
            while (length < bytes.Length && bytes[length] != 0) length++;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        private static bool SteamApiInit()
        {
            try { return SteamAPI_Init64(); }
            catch (DllNotFoundException) { use64 = false; return SteamAPI_Init32(); }
        }

        private static int GetHSteamUser()
        {
            return use64 ? SteamAPI_GetHSteamUser64() : SteamAPI_GetHSteamUser32();
        }

        private static int GetHSteamPipe()
        {
            return use64 ? SteamAPI_GetHSteamPipe64() : SteamAPI_GetHSteamPipe32();
        }

        private static IntPtr GetSteamClient()
        {
            return use64 ? SteamClient64() : SteamClient32();
        }

        private static IntPtr GetFriends(IntPtr client, int user, int pipe, string version)
        {
            return use64 ? GetFriends64(client, user, pipe, version) : GetFriends32(client, user, pipe, version);
        }

        private static void SetRichPresence(string key, string value)
        {
            bool result = use64 ? SetRichPresence64(friends, key, value) : SetRichPresence32(friends, key, value);
            if (!result) throw new InvalidOperationException("SteamAPI_ISteamFriends_SetRichPresence failed for " + key + ".");
        }

        private static void ClearRichPresence()
        {
            if (friends == IntPtr.Zero) return;
            if (use64) ClearRichPresence64(friends); else ClearRichPresence32(friends);
            PlatformService.SetRichPresenceVisibility(false);
        }

        private static void RegisterCallback(IntPtr callback, int identity)
        {
            if (use64) RegisterCallback64(callback, identity); else RegisterCallback32(callback, identity);
        }

        private static void UnregisterCallback(IntPtr callback)
        {
            if (use64) UnregisterCallback64(callback); else UnregisterCallback32(callback);
        }

        [DllImport("steam_api64", EntryPoint = "SteamAPI_Init", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SteamAPI_Init64();
        [DllImport("steam_api64", EntryPoint = "SteamAPI_GetHSteamUser", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamUser64();
        [DllImport("steam_api64", EntryPoint = "SteamAPI_GetHSteamPipe", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamPipe64();
        [DllImport("steam_api64", EntryPoint = "SteamClient", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamClient64();
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamClient_GetISteamFriends", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetFriends64(IntPtr client, int user, int pipe,
            [MarshalAs(UnmanagedType.LPStr)] string version);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamFriends_SetRichPresence", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SetRichPresence64(IntPtr instance,
            [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_ISteamFriends_ClearRichPresence", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ClearRichPresence64(IntPtr instance);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_RegisterCallback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RegisterCallback64(IntPtr callback, int identity);
        [DllImport("steam_api64", EntryPoint = "SteamAPI_UnregisterCallback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void UnregisterCallback64(IntPtr callback);

        [DllImport("steam_api", EntryPoint = "SteamAPI_Init", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SteamAPI_Init32();
        [DllImport("steam_api", EntryPoint = "SteamAPI_GetHSteamUser", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamUser32();
        [DllImport("steam_api", EntryPoint = "SteamAPI_GetHSteamPipe", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamPipe32();
        [DllImport("steam_api", EntryPoint = "SteamClient", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamClient32();
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamClient_GetISteamFriends", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetFriends32(IntPtr client, int user, int pipe,
            [MarshalAs(UnmanagedType.LPStr)] string version);
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamFriends_SetRichPresence", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SetRichPresence32(IntPtr instance,
            [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport("steam_api", EntryPoint = "SteamAPI_ISteamFriends_ClearRichPresence", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ClearRichPresence32(IntPtr instance);
        [DllImport("steam_api", EntryPoint = "SteamAPI_RegisterCallback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RegisterCallback32(IntPtr callback, int identity);
        [DllImport("steam_api", EntryPoint = "SteamAPI_UnregisterCallback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void UnregisterCallback32(IntPtr callback);
    }
}
