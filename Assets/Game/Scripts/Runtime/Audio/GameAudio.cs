using System;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Gacha;
using Deadlock.Game;
using Deadlock.Match;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Deadlock.Audio
{
    /// <summary>
    /// The game's one sound player (Stage 39), made at start and kept across scenes. It plays the sounds of
    /// <see cref="SoundCatalog"/> from a fixed set of voices (Unity's AudioSource; no extra library):
    /// <list type="bullet">
    /// <item>own sounds flat (2D), others' in the world (3D: linear fall-off to the sound's range, left / right, and
    /// each wall or floor between the listener and the sound halves it and dulls it, as the game's footstep hearing
    /// does), at most <see cref="SoundSpec.MaxVoices"/> copies of one sound and <see cref="VoiceCount"/> in all;</item>
    /// <item>the music of the current state (<see cref="BgmDirector"/>), streamed, crossfaded on a change;</item>
    /// <item>the volume settings (<see cref="SoundSettings"/>) applied at once to everything playing;</item>
    /// <item>the delayed spectating's sound events (<see cref="ISpectateSoundSink"/>), offline and online.</item>
    /// </list>
    /// Sound only: nothing here feeds the game's rules (hearing, hits, the network). A sound without its file is
    /// silence and recorded as not heard (<see cref="Recent"/>), never an error. Not made on a server without graphics.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour, ISpectateSoundSink
    {
        public const int VoiceCount = 24;
        public const int RecentLimit = 256;
        public const float BgmFadeSeconds = 1.5f;
        /// <summary>Each wall or floor between halves a world sound (as <see cref="Map.EnemyDetector"/> does for hearing).</summary>
        public const float ObstacleAttenuation = 0.5f;
        public const int MaxObstacles = 4;
        /// <summary>Layers that block sound (the level geometry, as the floor map's hearing uses).</summary>
        public static int OccluderMask = 1;

        public static GameAudio Instance { get; private set; }

        /// <summary>Tests: where sound files come from instead of the Resources folders (null: the Resources folders).</summary>
        public static Func<string, AudioClip> ClipSource;

        /// <summary>The music asked for by a scene that runs its own match (the online scene); null: none.</summary>
        public static Func<BgmState?> StateProvider;

        public struct PlayRecord
        {
            public string Id;
            public float Time;
            /// <summary>False: the sound has no file (silence).</summary>
            public bool Heard;
            public bool Spatial;
            public Vector3 Position;
            public float Volume;
            public float Pitch;
            public int Obstacles;
        }

        public struct VoiceState
        {
            public bool Busy;
            public string Id;
            public float Started;
            public int Importance;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter LowPass;
            public string Id;
            public float Started;
            public int Importance;
            public SoundGroup Group;
            public float BaseVolume;
        }

        readonly List<Voice> voices = new List<Voice>();
        readonly VoiceState[] voiceStates = new VoiceState[VoiceCount];
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly RaycastHit[] hits = new RaycastHit[8];
        /// <summary>The sounds asked for, newest last (bounded), heard or not: for checks.</summary>
        public readonly List<PlayRecord> Recent = new List<PlayRecord>();
        /// <summary>Sound ids asked for whose file does not exist.</summary>
        public readonly HashSet<string> Missing = new HashSet<string>();

        AudioSource bgmA, bgmB;
        AudioSource[] bgmSources;
        AudioSource bgmCurrent;
        float bgmFade = 1f, bgmOutFade;
        bool bgmFromResources, bgmOldFromResources;
        GameObject fallbackListener;
        AudioListener listener;
        float nextListenerCheck;
        TrainingSession training;
        TrainingResult trainingResult = TrainingResult.Playing;
        MatchPhase lastPhase = (MatchPhase)(-1);
        int lastCountdown = -1;
        GachaResult lastReward;

        public BgmState Bgm { get; private set; } = BgmState.None;
        /// <summary>The current music has a file and is playing.</summary>
        public bool BgmHeard => bgmCurrent != null && bgmCurrent.isPlaying;
        public AudioSource BgmSource => bgmCurrent;
        public AudioListener Listener => listener;
        public bool FallbackListenerActive => fallbackListener != null && fallbackListener.activeSelf;

        static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
            SceneManager.sceneLoaded += OnAnySceneLoaded;
            if (Ensure() != null)
                Instance.OnScene();
        }

        static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single && Ensure() != null)
                Instance.OnScene();
        }

        /// <summary>The sound player, made if it does not exist yet (null on a server without graphics).</summary>
        public static GameAudio Ensure()
        {
            if (Instance != null || Headless)
                return Instance;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            return go.AddComponent<GameAudio>();
        }

        // ------------------------------------------------------------------------------------------- playing

        /// <summary>An own sound of the player, heard flat (no place).</summary>
        public static bool Play(string id) => Instance != null && Instance.Emit(id, null, null, null);

        /// <summary>A sound at a place of the world (heard from the listener; flat if its spec is not spatial).</summary>
        public static bool PlayAt(string id, Vector3 position) => Instance != null && Instance.Emit(id, position, null, null);

        /// <summary>A screen sound (UI volume).</summary>
        public static bool Ui(string id) => Play(id);

        /// <summary>The sound of a shot of a weapon (the knife's swing for a knife).</summary>
        public static string ShotOf(string weaponId)
        {
            var entry = string.IsNullOrEmpty(weaponId) ? null : WeaponCatalog.Load()?.Find(weaponId);
            return entry != null && entry.IsKnife ? SoundCatalog.KnifeSwing : SoundCatalog.Fire(weaponId);
        }

        /// <summary>
        /// The voice a new sound takes: the oldest copy of the same sound when it already has
        /// <paramref name="maxVoices"/> copies; else a free voice; else the oldest busy voice of lower or equal
        /// importance; else none (-1: the new sound is not played).
        /// </summary>
        public static int PickVoice(IReadOnlyList<VoiceState> states, string id, int maxVoices, int importance)
        {
            int same = 0, oldestSame = -1, free = -1, oldestLower = -1;
            for (int i = 0; i < states.Count; i++)
            {
                var s = states[i];
                if (!s.Busy)
                {
                    if (free < 0)
                        free = i;
                    continue;
                }
                if (s.Id == id)
                {
                    same++;
                    if (oldestSame < 0 || s.Started < states[oldestSame].Started)
                        oldestSame = i;
                }
                if (s.Importance <= importance && (oldestLower < 0 || s.Started < states[oldestLower].Started))
                    oldestLower = i;
            }
            if (same >= Mathf.Max(1, maxVoices))
                return oldestSame;
            return free >= 0 ? free : oldestLower;
        }

        /// <summary>The level left after <paramref name="obstacles"/> walls or floors.</summary>
        public static float OcclusionGain(int obstacles) => Mathf.Pow(ObstacleAttenuation, Mathf.Clamp(obstacles, 0, MaxObstacles));

        /// <summary>The low-pass cut-off behind <paramref name="obstacles"/> walls or floors (0: not dulled).</summary>
        public static float OcclusionCutoff(int obstacles) => obstacles <= 0 ? 0f : Mathf.Max(800f, 22000f * Mathf.Pow(0.2f, Mathf.Min(obstacles, MaxObstacles)));

        /// <param name="position">null: flat.</param>
        /// <param name="occlusionFrom">Where the walls are counted from (null: the listener).</param>
        /// <param name="occlusionTo">… and to (null: the sound's place).</param>
        bool Emit(string id, Vector3? position, Vector3? occlusionFrom, Vector3? occlusionTo)
        {
            if (string.IsNullOrEmpty(id) || !SoundCatalog.TryGet(id, out var spec))
                return false;
            var clip = Clip(SoundCatalog.SePath(id));
            bool spatial = spec.Spatial && position.HasValue;
            int obstacles = 0;
            if (spatial)
            {
                var from = occlusionFrom ?? ListenerPosition();
                obstacles = CountObstacles(from, occlusionTo ?? position.Value);
            }
            float pitch = 1f + (spec.PitchJitter > 0f ? UnityEngine.Random.Range(-spec.PitchJitter, spec.PitchJitter) : 0f);
            float baseVolume = Mathf.Clamp01(spec.Volume) * OcclusionGain(obstacles);
            Record(new PlayRecord { Id = id, Time = Time.unscaledTime, Heard = clip != null, Spatial = spatial, Position = position ?? Vector3.zero,
                Volume = baseVolume * SoundSettings.Gain(spec.Group), Pitch = pitch, Obstacles = obstacles });
            if (clip == null)
            {
                Missing.Add(id);
                return false;
            }
            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                voiceStates[i] = new VoiceState { Busy = v.Source.isPlaying, Id = v.Id, Started = v.Started, Importance = v.Importance };
            }
            int pick = PickVoice(voiceStates, id, spec.MaxVoices, spec.Importance);
            if (pick < 0)
                return false;
            var voice = voices[pick];
            var src = voice.Source;
            src.Stop();
            voice.Id = id;
            voice.Started = Time.unscaledTime;
            voice.Importance = spec.Importance;
            voice.Group = spec.Group;
            voice.BaseVolume = baseVolume;
            src.clip = clip;
            src.pitch = pitch;
            src.volume = baseVolume * SoundSettings.Gain(spec.Group);
            src.spatialBlend = spatial ? 1f : 0f;
            src.minDistance = Mathf.Max(0.1f, spec.MinDistance);
            src.maxDistance = Mathf.Max(src.minDistance + 0.1f, spec.MaxDistance);
            src.transform.position = spatial ? position.Value : ListenerPosition();
            float cutoff = spatial ? OcclusionCutoff(obstacles) : 0f;
            voice.LowPass.enabled = cutoff > 0f;
            if (cutoff > 0f)
                voice.LowPass.cutoffFrequency = cutoff;
            src.Play();
            return true;
        }

        void Record(PlayRecord r)
        {
            Recent.Add(r);
            if (Recent.Count > RecentLimit)
                Recent.RemoveAt(0);
        }

        int CountObstacles(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float length = d.magnitude;
            if (length < 0.05f)
                return 0;
            int n = Physics.RaycastNonAlloc(from, d / length, hits, length, OccluderMask, QueryTriggerInteraction.Ignore);
            int count = 0;
            for (int i = 0; i < n; i++)
                if (hits[i].distance < length - 0.3f) // the sound's own body is not a wall
                    count++;
            return Mathf.Min(count, MaxObstacles);
        }

        Vector3 ListenerPosition() => listener != null ? listener.transform.position : transform.position;

        AudioClip Clip(string path)
        {
            if (clips.TryGetValue(path, out var clip))
                return clip;
            clip = ClipSource != null ? ClipSource(path) : Resources.Load<AudioClip>(path);
            clips[path] = clip;
            return clip;
        }

        /// <summary>Forgets the loaded sound files (tests change <see cref="ClipSource"/>).</summary>
        public void ResetClips()
        {
            foreach (var v in voices)
            {
                v.Source.Stop();
                v.Source.clip = null;
            }
            StopBgm();
            clips.Clear();
            Missing.Clear();
            Recent.Clear();
        }

        /// <summary>Loads the short sounds once at start, so the first shot is not late (they are small and kept).</summary>
        void PreloadSe()
        {
            foreach (var id in SoundCatalog.Ids)
                Clip(SoundCatalog.SePath(id));
        }

        // ------------------------------------------------------------------------------------------- spectating

        /// <summary>
        /// A past sound for the spectator: placed around the real listener as it was around the spectator's past eyes
        /// (<paramref name="listenerPosition"/>, <paramref name="listenerRotation"/>), with the walls counted between
        /// those eyes and the sound.
        /// </summary>
        public void Play(ReplayEvent e, Vector3 listenerPosition, Quaternion listenerRotation)
        {
            string id = e.Kind switch
            {
                ReplayEventKind.Gunshot => ShotOf(e.WeaponId),
                ReplayEventKind.Footstep => SoundCatalog.Footstep,
                ReplayEventKind.Reload => SoundCatalog.ReloadStart,
                ReplayEventKind.Death => SoundCatalog.Death,
                _ => null,
            };
            var real = listener != null ? listener.transform : transform;
            var relative = Quaternion.Inverse(listenerRotation) * (e.Position - listenerPosition);
            Emit(id, real.position + real.rotation * relative, listenerPosition, e.Position);
        }

        // ------------------------------------------------------------------------------------------- music

        public void SetBgm(BgmState state)
        {
            if (state == Bgm)
                return;
            Bgm = state;
            var old = bgmCurrent;
            var next = old == bgmA ? bgmB : bgmA;
            if (old != null)
            {
                bgmOutFade = bgmFade;
                bgmOldFromResources = bgmFromResources;
            }
            bgmCurrent = null;
            bgmFade = 0f;
            if (state == BgmState.None)
                return;
            string path = SoundCatalog.BgmPath(state);
            var clip = ClipSource != null ? ClipSource(path) : Resources.Load<AudioClip>(path);
            Record(new PlayRecord { Id = path, Time = Time.unscaledTime, Heard = clip != null });
            if (clip == null)
            {
                Missing.Add(path);
                return;
            }
            bgmFromResources = ClipSource == null;
            next.Stop();
            next.clip = clip;
            next.loop = BgmDirector.Loops(state);
            next.volume = 0f;
            next.Play();
            bgmCurrent = next;
        }

        void StopBgm()
        {
            bgmA.Stop();
            bgmB.Stop();
            bgmA.clip = bgmB.clip = null;
            bgmCurrent = null;
            Bgm = BgmState.None;
        }

        void UpdateBgm()
        {
            float step = Time.unscaledDeltaTime / BgmFadeSeconds;
            float gain = SoundSettings.Gain(SoundGroup.Bgm) * BgmDirector.Level(Bgm);
            if (bgmCurrent != null)
            {
                bgmFade = Mathf.Min(1f, bgmFade + step);
                bgmCurrent.volume = bgmFade * gain;
            }
            if ((bgmA != bgmCurrent && bgmA.isPlaying) || (bgmB != bgmCurrent && bgmB.isPlaying))
                bgmOutFade = Mathf.Max(0f, bgmOutFade - step);
            foreach (var old in bgmSources)
            {
                if (old == bgmCurrent || !old.isPlaying)
                    continue;
                old.volume = Mathf.Min(old.volume, bgmOutFade * SoundSettings.Gain(SoundGroup.Bgm));
                if (bgmOutFade <= 0f)
                {
                    old.Stop();
                    var clip = old.clip;
                    old.clip = null;
                    if (bgmOldFromResources && clip != null)
                        Resources.UnloadAsset(clip); // the streamed music is let go once it has faded out
                }
            }
        }

        BgmState DesiredBgm()
        {
            var flow = MatchFlow.Instance;
            if (flow != null && flow.Session != null && flow.Session.Phase != MatchPhase.Cancelled)
                return BgmDirector.ForMatch(flow.Session.Phase);
            if (trainingResult != TrainingResult.Playing)
                return BgmDirector.ForTraining(trainingResult);
            var provided = StateProvider?.Invoke();
            if (provided.HasValue)
                return provided.Value;
            return BgmDirector.ForScene(SceneManager.GetActiveScene().name);
        }

        // ------------------------------------------------------------------------------------------- the game

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            for (int i = 0; i < VoiceCount; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.dopplerLevel = 0f;
                src.spread = 0f;
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.enabled = false;
                voices.Add(new Voice { Source = src, LowPass = lp, Started = -1f });
            }
            bgmA = MakeBgmSource("MusicA");
            bgmB = MakeBgmSource("MusicB");
            bgmSources = new[] { bgmA, bgmB };
            fallbackListener = new GameObject("FallbackListener");
            fallbackListener.SetActive(false);
            fallbackListener.transform.SetParent(transform, false);
            fallbackListener.AddComponent<AudioListener>();
            SoundSettings.Changed += ApplyVolumes;
            MatchReplay.EventAdded += OnMatchSound;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            if (MatchSpectator.SoundSink == null)
                MatchSpectator.SoundSink = this;
            PreloadSe();
        }

        AudioSource MakeBgmSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            src.volume = 0f;
            src.priority = 0; // never taken by the voices
            return src;
        }

        void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;
            SoundSettings.Changed -= ApplyVolumes;
            MatchReplay.EventAdded -= OnMatchSound;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (ReferenceEquals(MatchSpectator.SoundSink, this))
                MatchSpectator.SoundSink = null;
        }

        void ApplyVolumes()
        {
            foreach (var v in voices)
                if (v.Source.isPlaying)
                    v.Source.volume = v.BaseVolume * SoundSettings.Gain(v.Group);
            // the music follows in UpdateBgm (this frame)
        }

        /// <summary>The output changed (headphones in / out, a call ended): the music starts again where it was.</summary>
        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (bgmCurrent != null && bgmCurrent.clip != null && !bgmCurrent.isPlaying)
                bgmCurrent.Play();
        }

        void OnScene()
        {
            if (training != null)
                training.Finished -= OnTrainingFinished;
            training = FindAnyObjectByType<TrainingSession>();
            trainingResult = TrainingResult.Playing;
            if (training != null)
                training.Finished += OnTrainingFinished;
            lastPhase = (MatchPhase)(-1);
            lastCountdown = -1;
            CheckListener(true);
            var motor = listener != null ? listener.GetComponentInParent<Player.FirstPersonMotor>() : null;
            if (motor != null && motor.GetComponent<PlayerAudioBinder>() == null)
                motor.gameObject.AddComponent<PlayerAudioBinder>();
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null)
            {
                menu.Opened -= OnMenuOpened;
                menu.Opened += OnMenuOpened;
                menu.Closed -= OnMenuClosed;
                menu.Closed += OnMenuClosed;
            }
            SetBgm(DesiredBgm());
        }

        static void OnMenuOpened() => Ui(SoundCatalog.MenuOpen);

        static void OnMenuClosed() => Ui(SoundCatalog.MenuClose);

        void OnTrainingFinished(TrainingResult result)
        {
            trainingResult = result;
            Play(result == TrainingResult.Cleared ? SoundCatalog.Victory : SoundCatalog.Defeat);
        }

        /// <summary>The offline match's other participants (NPCs): their shots, footsteps, reloads and deaths, live.</summary>
        void OnMatchSound(ReplayEvent e)
        {
            var flow = MatchFlow.Instance;
            if (flow == null || flow.LocalSlot == null || e.SlotId == flow.LocalSlot.Id || flow.Current == MatchFlow.View.Spectating)
                return; // the player's own sounds come from the player; a spectator hears the past instead
            string id = e.Kind switch
            {
                ReplayEventKind.Gunshot => ShotOf(e.WeaponId),
                ReplayEventKind.Footstep => SoundCatalog.Footstep,
                ReplayEventKind.Reload => SoundCatalog.ReloadStart,
                ReplayEventKind.Death => SoundCatalog.Death,
                _ => null,
            };
            PlayAt(id, e.Position);
        }

        void UpdateMatchCues()
        {
            var flow = MatchFlow.Instance;
            if (flow == null || flow.Session == null)
            {
                lastPhase = (MatchPhase)(-1);
                return;
            }
            var phase = flow.Session.Phase;
            if (phase == MatchPhase.Countdown && !flow.LocalSlot.Departed)
            {
                int n = Mathf.CeilToInt(flow.Session.CountdownRemaining(flow.Now));
                if (n != lastCountdown && n > 0)
                    Play(SoundCatalog.CountdownTick);
                lastCountdown = n;
            }
            if (phase != lastPhase && phase == MatchPhase.Playing && lastPhase == MatchPhase.Countdown)
                Play(SoundCatalog.MatchStart);
            lastPhase = phase;
            if (flow.Reward != lastReward)
            {
                lastReward = flow.Reward;
                if (lastReward != null && lastReward.Ok)
                    Ui(SoundCatalog.Reward);
            }
        }

        /// <summary>
        /// The scene's listener (the player's camera); scenes without one (the home, the online scene before its
        /// camera exists) use the player's own one here, at the main camera.
        /// </summary>
        void CheckListener(bool now)
        {
            if (!now && Time.unscaledTime < nextListenerCheck && listener != null && listener.isActiveAndEnabled)
                return;
            nextListenerCheck = Time.unscaledTime + 0.5f;
            AudioListener found = null;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled && l.gameObject != fallbackListener)
                {
                    found = l;
                    break;
                }
            fallbackListener.SetActive(found == null);
            listener = found != null ? found : fallbackListener.GetComponent<AudioListener>();
        }

        void Update()
        {
            CheckListener(false);
            if (FallbackListenerActive)
            {
                var cam = Camera.main;
                if (cam != null)
                    fallbackListener.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
            }
            UpdateMatchCues();
            SetBgm(DesiredBgm());
            UpdateBgm();
        }
    }
}
