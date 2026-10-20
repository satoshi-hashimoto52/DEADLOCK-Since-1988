using System.Collections;
using System.Linq;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Network;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Online test scene in one Editor process: the host and a host-spawned stand-in player ("dummy").
    /// Real two-process / two-device connections are checked by NetworkCheckRunner (Tools/net-check-local.sh).
    /// </summary>
    public class OnlineTests : FiringRangeTestBase
    {
        NetworkSession session;
        OnlineSessionUi ui;
        GameMenu menu;

        IEnumerator LoadOnline()
        {
            yield return LoadScene(InputMode.Desktop, GameScenes.Online);
            session = NetworkSession.Instance;
            ui = Object.FindAnyObjectByType<OnlineSessionUi>();
            menu = Object.FindAnyObjectByType<GameMenu>();
        }

        [UnityTearDown]
        public IEnumerator EndSession()
        {
            if (session != null && session.IsActive)
                session.Leave();
            for (int i = 0; i < 5; i++)
                yield return null;
        }

        IEnumerator Host()
        {
            ui.HostButton.onClick.Invoke();
            for (int i = 0; i < 60 && NetworkAvatar.All.Count == 0; i++)
                yield return null;
            yield return null;
        }

        NetworkAvatar Mine => NetworkAvatar.All.Single(a => a.IsLocalPlayerAvatar);

        NetworkAvatar SpawnDummy(Vector3 position, float yaw)
        {
            var go = Object.Instantiate(session.Manager.NetworkConfig.PlayerPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            var dummy = go.GetComponent<NetworkAvatar>();
            dummy.MarkAsDummy();
            go.GetComponent<NetworkObject>().Spawn(true);
            SetDummy(dummy, position, yaw, Posture.Standing);
            return dummy;
        }

        static void SetDummy(NetworkAvatar dummy, Vector3 position, float yaw, Posture posture)
        {
            var s = dummy.State.Value;
            s.Position = position;
            s.Yaw = yaw;
            s.Posture = (byte)posture;
            dummy.State.Value = s;
            dummy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        void AimAt(Vector3 target)
        {
            Vector3 d = target - Camera.main.transform.position;
            motor.SetYawPitch(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
        }

        IEnumerator ShootOnce(Vector3 target)
        {
            Press(mouse.rightButton, queueEventOnly: true); // ADS: tight spread
            yield return Wait(0.3f);
            AimAt(target);
            yield return Tap(mouse.leftButton);
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.3f);
        }

        [UnityTest]
        public IEnumerator Lobby_Host_Leave_Rehost_NoDuplicates_MenuDoesNotPause()
        {
            yield return LoadOnline();
            Assert.IsNull(Object.FindAnyObjectByType<OfflineSessionPause>(), "No offline pause in the online scene");
            Assert.IsTrue(ui.Lobby.activeSelf);
            Assert.IsTrue(router.IsBlocked, "No play while in the lobby");

            yield return Host();
            Assert.AreEqual(SessionState.Hosting, session.State);
            Assert.IsFalse(ui.Lobby.activeSelf);
            Assert.IsFalse(router.IsBlocked);
            Assert.AreEqual(1, NetworkAvatar.All.Count);
            Assert.AreEqual(1, NetworkAvatar.LocalBindings);
            Assert.IsFalse(Mine.View.IsVisible, "Own model hidden");
            Assert.AreEqual(NetworkSession.OwnHitboxLayer, Mine.Hitboxes.Body.gameObject.layer);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);

            // Menu online: no pause, only this player's input stops.
            menu.Open();
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(router.IsBlocked);
            menu.Close();
            yield return null;

            // Leave → lobby; host again → still exactly one avatar and one binding.
            session.Leave();
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.AreEqual(0, NetworkAvatar.All.Count);
            Assert.AreEqual(0, NetworkAvatar.LocalBindings);
            Assert.IsTrue(ui.Lobby.activeSelf);
            Assert.AreEqual("ホストを終了しました", session.LastMessage);
            yield return Host();
            Assert.AreEqual(1, NetworkAvatar.All.Count);
            Assert.AreEqual(1, NetworkAvatar.LocalBindings);

            // One shot = one request (no duplicated subscriptions after re-hosting).
            yield return ShootOnce(player.transform.position + new Vector3(0f, 1.2f, 10f));
            Assert.AreEqual(1, Mine.ShotsRequested);
            Assert.AreEqual(1, Mine.ServerShotsAccepted);
            Assert.AreEqual(gun.Definition.magazineSize - 1, gun.Ammo.Loaded, "Ammo from the host");
        }

        [UnityTest]
        public IEnumerator HostDecidesDamage_OneHitPerShot_NoSelfHit_WallBlocks_PostureHitVolumes_Knife()
        {
            yield return LoadOnline();
            yield return Host();
            var dummy = SpawnDummy(new Vector3(4f, 0.05f, 2f), 180f);
            Teleport(new Vector3(4f, 0.05f, -2f));
            yield return Wait(0.2f);

            yield return ShootOnce(dummy.transform.position + Vector3.up * 1.15f);
            Assert.AreEqual(75f, dummy.Health.Value, "One shot = one hit = 25 (head and body never both)");
            Assert.AreEqual(100f, Mine.Health.Value, "No self-hit");

            // Prone: the hit volume lies on the floor; a shot at standing chest height passes over it.
            SetDummy(dummy, dummy.transform.position, 180f, Posture.Prone);
            yield return Wait(0.1f);
            yield return ShootOnce(dummy.transform.position + Vector3.up * 1.15f);
            Assert.AreEqual(75f, dummy.Health.Value, "Over a prone player");
            yield return ShootOnce(dummy.transform.position + new Vector3(0f, 0.3f, -0.3f));
            Assert.AreEqual(50f, dummy.Health.Value, "A low shot hits the prone player");

            // Behind the centre wall: blocked.
            SetDummy(dummy, new Vector3(0f, 0.05f, 4f), 180f, Posture.Standing);
            Teleport(new Vector3(0f, 0.05f, -4f));
            yield return Wait(0.2f);
            yield return ShootOnce(dummy.transform.position + Vector3.up * 1.15f);
            Assert.AreEqual(50f, dummy.Health.Value, "Walls stop shots");

            // Knife: 50, one target per swing.
            SetDummy(dummy, new Vector3(4f, 0.05f, 2f), 180f, Posture.Standing);
            Teleport(new Vector3(4f, 0.05f, 0.6f));
            yield return Wait(0.2f);
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            AimAt(dummy.transform.position + Vector3.up * 1.15f);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(0f, dummy.Health.Value);
            Assert.IsTrue(dummy.Dead.Value);
            Assert.IsFalse(dummy.Hitboxes.Body.enabled, "Dead players stop taking hits");
        }

        [UnityTest]
        public IEnumerator HostRefuses_DeadShooter_FarOrWalledOrigin_RespawnsOnRequest()
        {
            yield return LoadOnline();
            yield return Host();
            yield return null;
            var dummy = SpawnDummy(new Vector3(0f, 0.05f, 4f), 180f);
            Teleport(new Vector3(0f, 0.05f, -4f));
            yield return Wait(0.2f);
            Vector3 hostChest = player.transform.position + Vector3.up * 1.15f;

            // A claimed origin right in front of the host, while the shooter stands behind the wall: the host
            // starts the shot from the shooter's eye instead, and the wall stops it.
            dummy.ServerFire(hostChest + Vector3.forward * 0.8f, Vector3.back);
            yield return Wait(0.3f);
            Assert.AreEqual(100f, Mine.Health.Value);
            Assert.AreEqual(1, dummy.ServerShotsAccepted);

            // Dead players cannot shoot.
            SetDummy(dummy, new Vector3(4f, 0.05f, 2f), 180f, Posture.Standing);
            Teleport(new Vector3(4f, 0.05f, -2f));
            yield return Wait(0.2f);
            dummy.ServerTakeDamage(1000f, dummy.transform.position);
            Assert.IsTrue(dummy.Dead.Value);
            Vector3 eye = dummy.EyePosition;
            dummy.ServerFire(eye, (player.transform.position + Vector3.up * 1.15f - eye).normalized);
            yield return Wait(0.3f);
            Assert.AreEqual(1, dummy.ServerShotsAccepted);
            Assert.AreEqual(100f, Mine.Health.Value);

            // Respawn on request: full HP, alive, ammo back.
            dummy.ServerRespawn();
            yield return null;
            Assert.IsFalse(dummy.Dead.Value);
            Assert.AreEqual(100f, dummy.Health.Value);
            Assert.AreEqual(gun.Definition.magazineSize, dummy.ServerWeapons.Ammo.Loaded);
        }

        [UnityTest]
        public IEnumerator LocalPlayer_DiesAndRespawns_ThroughTheHost()
        {
            yield return LoadOnline();
            yield return Host();
            Mine.ServerTakeDamage(40f, Vector3.zero);
            yield return null;
            Assert.AreEqual(60f, player.GetComponent<PlayerHealth>().Health, "Local HP follows the host");
            Mine.ServerTakeDamage(100f, Vector3.zero);
            yield return null;
            Assert.IsTrue(player.GetComponent<PlayerHealth>().IsDead);
            Assert.IsTrue(router.IsBlocked);
            yield return null;
            Assert.IsTrue(ui.RespawnButton.gameObject.activeSelf, "Respawn (test) button shown when dead");
            ui.RespawnButton.onClick.Invoke();
            yield return Wait(0.2f);
            Assert.IsFalse(player.GetComponent<PlayerHealth>().IsDead);
            Assert.AreEqual(100f, player.GetComponent<PlayerHealth>().Health);
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(ui.RespawnButton.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Touch_AdsLinkedFire_GoesThroughTheHost_AndSyncsTheActualAdsState()
        {
            yield return LoadOnline();
            yield return Host();
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            router.SetMode(InputMode.Touch);
            var touchscreen = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Touchscreen>();
            yield return null;
            Vector2 fire = ScreenCenter((RectTransform)GameObject.Find("FireButton").transform);
            int requested = Mine.ShotsRequested;
            BeginTouch(1, fire, queueEventOnly: true, screen: touchscreen);
            yield return Wait(gun.Definition.adsTime * 0.5f);
            Assert.AreEqual(requested, Mine.ShotsRequested, "No shot request before fully aimed");
            yield return Wait(gun.Definition.adsTime + 0.2f);
            Assert.IsTrue(Mine.State.Value.Has(AvatarState.Flag.Aiming), "The actual ADS state is synced");
            Assert.Greater(Mine.ShotsRequested, requested, "Shots go out as host requests");
            Assert.AreEqual(Mine.ShotsRequested, Mine.ServerShotsAccepted + Mine.ShotsRejected, "Every shot is decided by the host");
            Assert.Less(Mine.ServerWeapons.Ammo.Loaded, gun.Definition.magazineSize, "Ammo spent on the host");
            EndTouch(1, fire, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.2f);
            Assert.IsFalse(Mine.State.Value.Has(AvatarState.Flag.Aiming));
        }
    }
}
