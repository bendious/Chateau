//#define FIXED_SEED


using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


[DisallowMultipleComponent]
public class GameController : MonoBehaviour
{
	public LayoutGenerator.Node.Type m_type;

	public LayerMaskHelper m_layerDefault;
	public LayerMaskHelper m_layerWalls;
	public LayerMaskHelper m_layerOneWay;
	public LayerMaskHelper m_layerExterior;

	public List<AvatarController> m_avatars = new();

	public CinemachineVirtualCamera m_vCamMain;
	public CinemachineVirtualCamera m_vCamOverview;
	[SerializeField] private CinemachineTargetGroup m_ctGroupMain;
	public CinemachineTargetGroup m_ctGroupOverview;

	public DialogueController m_dialogueController;

	public WeightedObject<RoomController>[] m_entryRoomPrefabs;
	public WeightedObject<RoomController>[] m_roomPrefabs;
	public WeightedObject<RoomController>[] m_bossRoomPrefabs;
	public WeightedObject<GameObject>[] m_gatePrefabs;
	public WeightedObject<GameObject>[] m_lockPrefabs;
	public WeightedObject<GameObject>[] m_keyPrefabs;
	public WeightedObject<GameObject>[] m_cutbackPrefabs;
	public RoomType.DecorationInfo[] m_textPrefabs;
	public WeightedObject<RoomType>[] m_roomTypes;
	public WeightedObject<RoomType>[] m_roomTypesSecret;
	public WeightedObject<RoomType>[] m_roomTypesBoss;
	public WeightedObject<AIController>[] m_enemyPrefabs;
	public WeightedObject<AIController>[] m_npcPrefabs;
	[SerializeField] private WeightedObject<Dialogue>[] m_npcRoles;
	[SerializeField] private WeightedObject<Dialogue>[] m_npcAttitudes;
	public GameObject[] m_doorInteractPrefabs;
	public GameObject[] m_zoneFinishedIndicators;
	public GameObject[] m_upgradeIndicators;
	[SerializeField] private GameObject[] m_directionSigns;

	[SerializeField] private int m_specialRoomCount;
	public bool m_allowHiddenDestructibles = true;
	public bool m_allowCutbacks = true;
	[SerializeField] private bool m_waveSealing = false;
	public float m_zoneScalar = 1.0f;

	public const int m_zoneCount = 4; // TODO: derive?
	public const int m_hintsPerZone = 2;
	[SerializeField] private int m_narrowPathLength = m_zoneCount * m_hintsPerZone;

	public float m_difficultyMin = 0.0f;
	public float m_difficultyMax = 0.0f;

	[SerializeField] private GameObject m_loadingScreen;
	[SerializeField] private Image m_loadingIcon;
	public float m_fadeSeconds = 0.5f;
	[SerializeField] private Image m_timerUI;
	[SerializeField] private Animator m_timerAnimator;
	[SerializeField] private GameObject m_startUI;
	[SerializeField] private Canvas m_pauseUI;
	[SerializeField] private TMPro.TMP_Text m_quitText;
	[SerializeField] private PlayerInputManager m_inputManager;
	public Canvas m_gameOverUI;

	[SerializeField] private Dialogue m_introDialogue;

	public MaterialSystem m_materialSystem;
	public SavableFactory m_savableFactory;
	public LightFlicker m_lightFlickerMaster;

	public string m_mouseControlScheme = "Keyboard&Mouse"; // TODO: derive?

	[SerializeField] private WeightedObject<AudioClip>[] m_timerWarnSFX;
	public AudioClip m_victoryAudio;

	[SerializeField] private float m_waveSecondsMin = 45.0f;
	[SerializeField] private float m_waveSecondsMax = 90.0f;
	public float m_waveStartWeight = 1.0f;
	[SerializeField] private bool m_startWavesImmediately = false;
	[SerializeField] private int m_waveEnemyCountMax = int.MaxValue;
	[SerializeField] private float m_waveEscalationMin = 0.0f;
	[SerializeField] private float m_waveEscalationMax = 4.0f;
	[SerializeField] private float m_waveEnemyDelayMin = 0.5f;
	[SerializeField] private float m_waveEnemyDelayMax = 2.0f;


	[HideInInspector] public bool m_bossRoomSealed = false;


	public static bool IsSceneLoad { get; private set; }

	public static GameController Instance { get; private set; }

	public static Dialogue[] NpcDialogues(int index) => m_npcs[index].m_dialogues;
	public static Color NpcColor(int index) => m_npcs[index].m_color;

	public static int[] MerchantAcquiredCounts;
	public static int MerchantMaterials;

	public static int ZonesFinishedCount { get; private set; }

	public static Color[] NarrowPathColors { get; private set; }
	public int NarrowPathHintCount { get; set; }
	public bool OnNarrowPath { get; set; } = true;

	public static bool SecretFound(int index) => m_secretsFoundBitmask[index];
	public static void SetSecretFound(int index) => m_secretsFoundBitmask.Set(index, true);


	public RoomController[] SpecialRooms { get; private set; }

	public int NpcsTotal => m_npcs.Length; // NOTE that this is not available until after Start()
	public int NpcsInstantiatedCount => m_npcsInstantiated.Count;

	public IEnumerable<KinematicCharacter> AiTargets => m_avatars.Select(avatar => (KinematicCharacter)avatar).Concat(m_enemies).Concat(m_npcsInstantiated); // TODO: efficiency?

	public bool Victory { get; private set; }


	[SerializeField] private string[] m_savableTags;


	public static int Seed => m_seed;
	private static int m_seed;

	public static int SceneIndexPrev { get; private set; } = -1;

	private RoomController m_startRoom;

	private struct NpcInfo
	{
		public Color m_color;
		public Dialogue[] m_dialogues;
	}
	private static NpcInfo[] m_npcs;

	private CinemachineConfiner2D m_vCamMainConfiner;
	private CinemachineFramingTransposer m_vCamMainFramer;
	private float m_lookaheadTimeOrig;

	private bool m_usingMouse = false;

	private float m_waveWeight;
	private float m_waveWeightRemaining;
	private float m_nextWaveTime = 0.0f;
	private bool m_waveSpawningInProgress = false;

	private readonly List<AIController> m_enemies = new();
	private readonly List<AIController> m_enemiesInWave = new();
	private static int[] m_enemySpawnCounts;

	private readonly List<AIController> m_npcsInstantiated = new();

	private static readonly BitArray m_secretsFoundBitmask = new(sizeof(int) * 8); // TODO: avoid limiting to a single int?

	private static /*readonly*/ BitArray m_upgradesActiveBitmask = new(sizeof(int) * 8); // TODO: avoid limiting to a single int?
	private static /*readonly*/ int[] m_upgradeCounts = new int[Utility.EnumNumTypes<InteractUpgrade.Type>() - 1]; // NOTE the -1 since InteractUpgrade.Type.None is excluded


	private void Awake()
	{
		Instance = this;

#if !FIXED_SEED
		m_seed = Random.Range(int.MinValue, int.MaxValue); // TODO: don't use Random to seed Random?
#endif
		Random.InitState(m_seed);

		m_vCamMainConfiner = m_vCamMain.GetComponentInChildren<CinemachineConfiner2D>();
		m_vCamMainFramer = m_vCamMain.GetCinemachineComponent<CinemachineFramingTransposer>();
		m_lookaheadTimeOrig = m_vCamMainFramer.m_LookaheadTime;

		// TODO: use Animator on persistent object?
		float alpha = Mathf.Abs((Time.realtimeSinceStartup % 1.0f) - 0.5f) * 2.0f;
		m_loadingIcon.color = new(m_loadingIcon.color.r, m_loadingIcon.color.g, m_loadingIcon.color.b, alpha);
	}

	private void Start()
	{
		ObjectDespawn.OnExecute += OnObjectDespawn;

		m_waveWeight = m_waveStartWeight;

		LayoutGenerator generator = new(new(m_type));
		generator.Generate();

		// use generator to spawn rooms/locks/keys/items/etc.
		LayoutGenerator.Node parentPending = null;
		List<LayoutGenerator.Node> nodesPending = new();
		int roomCount = 0;
		int orderedLockIdx = 0;
		bool failed = generator.ForEachNodeDepthFirst(node =>
		{
			Debug.Assert(node.m_room == null && node.m_type != LayoutGenerator.Node.Type.TightCoupling && node.m_type != LayoutGenerator.Node.Type.AreaDivider);

			LayoutGenerator.Node parent = node.TightCoupleParent;
			if (parent != parentPending && nodesPending.Count > 0 && parentPending != null)
			{
				roomCount = AddRoomsForNodes(nodesPending.ToArray(), roomCount, ref orderedLockIdx);
				if (roomCount == 0)
				{
					return true;
				}
				nodesPending.Clear();
			}

			parentPending = parent;
			nodesPending.Add(node);
			return false;
		});
		failed = failed || AddRoomsForNodes(nodesPending.ToArray(), roomCount, ref orderedLockIdx) == 0;
		if (failed)
		{
#if FIXED_SEED
			++m_seed; // to prevent infinite failure loop
#endif
			Retry(true); // TODO: more efficient way to guarantee room spawning?
			return;
		}

		bool saveExists = Load();

		// first-time initializations
		if (m_npcs == null)
		{
			NpcsRandomize();
		}
		if (MerchantAcquiredCounts == null)
		{
			MerchantAcquiredCounts = new int[m_savableFactory.m_savables.Length]; // TODO: don't assume the same number/arrangement of savables in each scene?
		}
		if (NarrowPathColors == null)
		{
			NarrowPathColors = new Color[m_narrowPathLength];
			for (int i = 0; i < m_narrowPathLength; ++i)
			{
				NarrowPathColors[i] = Utility.ColorRandom(Color.black, Color.white, false); // NOTE that we don't have to worry about duplicate colors here since these are sequential and not presented simultaneously
			}
		}
		if (m_enemySpawnCounts == null)
		{
			m_enemySpawnCounts = new int[m_enemyPrefabs.Length]; // TODO: don't assume the same number/arrangement of enemies in each scene
		}

		// fill rooms
		int doorwayDepth = 0;
		int npcDepth = 0;
		m_startRoom.FinalizeRecursive(ref doorwayDepth, ref npcDepth);
		RoomController[] roomsHighToLow = m_startRoom.WithDescendants.OrderBy(room => -room.transform.position.y).ToArray();
		float roomWidthMin = roomsHighToLow.Min(room => room.Bounds.size.x);
		foreach (RoomController room in roomsHighToLow)
		{
			room.FinalizeTopDown(roomWidthMin);
		}

		// spawn progress indicator(s)
		if (m_specialRoomCount > 1)
		{
			List<InteractUpgrade> progressIndicatorsTemp = new();
			RoomController room = SpecialRooms[1];
			int indicatorIdx = 0;
			foreach (GameObject indicatorPrefab in m_zoneFinishedIndicators)
			{
				InteractUpgrade indicator = Instantiate(indicatorPrefab, room.InteriorPosition(0.0f, indicatorPrefab), Quaternion.identity, room.transform).GetComponent<InteractUpgrade>();
				if (indicatorIdx < ZonesFinishedCount)
				{
					indicator.ToggleActivation(true);
				}
				progressIndicatorsTemp.Add(indicator);
				++indicatorIdx;
			}
			InteractUpgrade[] progressIndicators = progressIndicatorsTemp.ToArray();

			// spawn upgrade indicator(s)
			indicatorIdx = 0;
			foreach (GameObject indicatorPrefab in m_upgradeIndicators)
			{
				InteractUpgrade interact = Instantiate(indicatorPrefab, room.InteriorPosition(0.0f, indicatorPrefab), Quaternion.identity, room.transform).GetComponent<InteractUpgrade>();
				interact.m_index = indicatorIdx;
				interact.m_sources = progressIndicators;
				if (m_upgradesActiveBitmask.Get(indicatorIdx))
				{
					interact.ToggleActivation(true);
				}
				++indicatorIdx;
			}
		}

		// spawn directional signs
		int signIdx = 0;
		foreach (GameObject signPrefab in m_directionSigns)
		{
			Vector3 signPos = m_startRoom.InteriorPosition(0.0f, signPrefab);
			GameObject sign = Instantiate(signPrefab, signPos, Quaternion.identity, m_startRoom.transform);

			// aim
			RoomController targetRoom = signIdx < m_specialRoomCount ? SpecialRooms[signIdx] : FindObjectsOfType<InteractScene>().First(interact => interact.DestinationIndex == signIdx - m_specialRoomCount + 1).transform.parent.GetComponent<RoomController>(); // TODO: less hardcoding?
			sign.transform.GetChild(0).transform.rotation = Utility.ZRotation(targetRoom.transform.position - signPos); // TODO: un-hardcode child index?

			++signIdx;
		}

		if (m_avatars.Count > 0)
		{
			// reapply upgrades to clear temporary intra-run boosts
			// NOTE that this is necessary since OnPlayerJoined() gets called before Start()/Load() for pre-existing avatars
			if (saveExists)
			{
				foreach (AvatarController avatar in m_avatars)
				{
					ApplyUpgrades(avatar);
				}
			}

			EnterAtDoor(m_avatars.ToArray());
		}

		if (!saveExists)
		{
			// TODO: move earlier?
			const string tutorialSceneName = "Tutorial"; // TODO: un-hardcode?
			if (SceneManager.GetActiveScene().name != tutorialSceneName)
			{
				IsSceneLoad = true; // to preempt LoadScene() setting SceneIndexPrev, which we don't want in this case
				StartCoroutine(LoadSceneCoroutine(tutorialSceneName, false, false));
				return;
			}

			m_dialogueController.Play(m_introDialogue.m_dialogue.RandomWeighted().m_lines, expressionSets: m_introDialogue.m_expressions); // TODO: take any preconditions into account?
		}

		if (m_startWavesImmediately)
		{
			StartWaves();
		}

		if ((m_quitText != null || m_avatars.Count <= 0) && m_inputManager != null)
		{
			m_inputManager.EnableJoining();
		}

		StartCoroutine(FadeCoroutine(false));

		IsSceneLoad = false;
	}

	private void OnDestroy()
	{
		IsSceneLoad = true;
		ObjectDespawn.OnExecute -= OnObjectDespawn;
		Simulation.Clear();
	}

	private void Update()
	{
		// update camera constraint/lookahead
		// TODO: efficiency?
		AvatarController liveAvatar = m_avatars.FirstOrDefault(avatar => avatar.IsAlive);
		RoomController constraintRoom = liveAvatar == null ? null : RoomFromPosition(liveAvatar.transform.position);
		bool isLooking = m_avatars.Any(avatar => avatar.IsAlive && avatar.IsLooking);
		bool unconstrained = constraintRoom == null || isLooking || m_avatars.Any(avatar => avatar.IsAlive && RoomFromPosition(avatar.transform.position) != constraintRoom);
		m_vCamMainConfiner.m_BoundingShape2D = unconstrained ? null : constraintRoom.GetComponentInChildren<PolygonCollider2D>();
		m_vCamMainFramer.m_LookaheadTime = isLooking ? 0.0f : m_lookaheadTimeOrig;

		Simulation.Tick();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via SendMessage() from PlayerInputManager component")]
	private void OnPlayerJoined(PlayerInput player)
	{
		// NOTE that we can place this here since OnPlayerJoined() is called even if the avatar object(s) is/are carried over from a previously loaded scene
		if (m_quitText != null && m_avatars.Count <= 0)
		{
			// move/update start text to indicate possibility of co-op
			m_startUI.GetComponentInChildren<TMPro.TMP_Text>().text = "2nd player: Press Start";
			RectTransform rectTf = m_startUI.GetComponent<RectTransform>();
			rectTf.anchorMin = Vector2.one;
			rectTf.anchorMax = Vector2.one;
			rectTf.pivot = Vector2.one;
		}
		else
		{
			m_startUI.SetActive(false);
		}
		if (!m_startWavesImmediately && m_enemyPrefabs.Length > 0 && m_waveSecondsMin > 0.0f && m_avatars.Count == 0)
		{
			StartWaves();
		}
		if (m_waveEscalationMax <= 0.0f)
		{
			Victory = true;
		}

		if (player.currentControlScheme == m_mouseControlScheme)
		{
			m_usingMouse = true;
			Cursor.visible = true;
		}

		// apply upgrades
		AvatarController avatarNew = player.GetComponent<AvatarController>();
		ApplyUpgrades(avatarNew);

		m_avatars.Add(avatarNew);
		// TODO: move closer to existing avatar(s)?

		// adjust camera UI/targeting
		// TODO: split screen when far apart?
		bool isFirst = true;
		foreach (AvatarController avatar in m_avatars)
		{
			if (!isFirst)
			{
				// re-anchor left-aligned UI elements to the right
				foreach (RectTransform tf in avatar.GetComponentsInChildren<RectTransform>(true))
				{
					if (tf.anchorMin.x == 0.0f && tf.anchorMax.x == 0.0f)
					{
						tf.anchorMin = new(1.0f, tf.anchorMin.y);
						tf.anchorMax = new(1.0f, tf.anchorMax.y);
						tf.pivot = new(1.0f, tf.pivot.y);
						tf.anchoredPosition *= new Vector2(-1.0f, 1.0f);
					}
				}
				if (m_quitText != null)
				{
					m_quitText.text = "Exit Co-op";
				}
				if (m_inputManager != null)
				{
					m_inputManager.DisableJoining(); // NOTE that we don't just use PlayerInputManager's max player count since that was giving an error when/after reaching the max
				}
			}

			// NOTE that we don't replace the whole m_Targets array in case a non-avatar object is also present
			AddCameraTargets(avatar.transform);

			isFirst = false;
		}

		if (m_startRoom != null)
		{
			EnterAtDoor(m_avatars.Last());
		}
	}

	private void OnPlayerLeft(PlayerInput player)
	{
		if (player.currentControlScheme == m_mouseControlScheme)
		{
			m_usingMouse = false; // TODO: don't assume only one mouse?
			Cursor.visible = false;
		}

		Simulation.Schedule<ObjectDespawn>().m_object = player.gameObject;
		m_avatars.Remove(player.GetComponent<AvatarController>());
		// TODO: clean up camera targeting?

		if (m_avatars.Count <= 1 && m_quitText != null)
		{
			m_quitText.text = "Quit";
		}
		if (m_inputManager != null)
		{
			m_inputManager.EnableJoining();
		}
	}

	private void OnApplicationFocus(bool focus)
	{
		if (focus)
		{
			Cursor.visible = m_usingMouse;
		}
	}

	private void OnApplicationQuit() => IsSceneLoad = true;


	public void AddCameraTargets(params Transform[] transforms) => AddCameraTargetsSized(0.0f, transforms);

	public void AddCameraTargetsSized(float size, params Transform[] transforms)
	{
		foreach (Transform tf in transforms)
		{
			m_ctGroupMain.RemoveMember(tf); // prevent duplication // TODO: necessary?
			m_ctGroupMain.AddMember(tf, 1.0f, size); // TODO: blend weight in?
			// TODO: auto-remove on target destruction?
		}
	}

	public void RemoveCameraTargets(params Transform[] transforms)
	{
		foreach (Transform tf in transforms)
		{
			m_ctGroupMain.RemoveMember(tf); // TODO: blend weight out?
		}
	}

	public RoomController RoomFromPosition(Vector2 position) => m_startRoom.FromPosition(position); // TODO: work even when disconnected from start room?

	public RoomController RandomReachableRoom(AIController ai, GameObject endpointObj, bool endpointIsStart) => m_startRoom.WithDescendants.Where(room => ai.Pathfind(endpointIsStart ? endpointObj : room.gameObject, endpointIsStart ? room.gameObject : endpointObj, Vector2.zero) != null).Random();

	public System.Tuple<List<Vector2>, float> Pathfind(GameObject start, GameObject target, float extentY = -1.0f, float upwardMax = float.MaxValue, Vector2 offsetMag = default, RoomController.PathFlags flags = RoomController.PathFlags.ObstructionCheck)
	{
		RoomController startRoom = RoomFromPosition(start.transform.position); // TODO: use closest bbox point?
		return startRoom == null ? null : startRoom.PositionPath(start, target, flags, extentY, upwardMax, offsetMag);
	}

	public void TogglePause(bool noTimeScale = false)
	{
		if (m_loadingScreen.activeSelf || m_gameOverUI.gameObject.activeSelf)
		{
			return;
		}

		Time.timeScale = noTimeScale || Time.timeScale == 0.0f ? 1.0f : 0.0f;
		StartCoroutine(ActivateMenuCoroutine(m_pauseUI, !m_pauseUI.gameObject.activeSelf));
		// NOTE that if the avatar is ever visible while paused, we should disable its script here to avoid continuing to update facing
	}

	public void EnemyAdd(AIController enemy)
	{
		EnemyAddInternal(m_enemies, enemy);
		// TODO: increment m_enemySpawnCounts[] if appropriate
	}

	public void EnemyAddToWave(AIController enemy) => EnemyAddInternal(m_enemiesInWave, enemy);

	public void NpcAdd(AIController npc) => m_npcsInstantiated.Add(npc);

	public float ActiveEnemiesWeight() => m_waveWeightRemaining + m_enemiesInWave.Aggregate(0.0f, (sum, enemy) => sum + enemy.m_difficulty);

	public bool ActiveEnemiesRemain() => m_waveSpawningInProgress || m_enemiesInWave.Count > 0;

	public bool EnemyTypeHasSpawned(int typeIndex) => m_enemySpawnCounts[typeIndex] > 0;

	public void RemoveUnreachableEnemies()
	{
		if (!m_waveSealing)
		{
			return;
		}

		// TODO: don't assume sealing works on an individual room basis?
		HashSet<RoomController> reachableRooms = new(m_avatars.Where(avatar => avatar.IsAlive).Select(avatar => RoomFromPosition(avatar.transform.position)));
		List<AIController> unreachableEnemies = new();
		HashSet<RoomController> roomsToUnseal = new();

		foreach (AIController enemy in m_enemiesInWave)
		{
			RoomController room = RoomFromPosition(enemy.transform.position);
			if (reachableRooms.Contains(room))
			{
				continue;
			}

			unreachableEnemies.Add(enemy);
			roomsToUnseal.Add(room);
		}
		m_enemiesInWave.RemoveAll(enemy => unreachableEnemies.Contains(enemy));

		if (m_enemiesInWave.Count <= 0)
		{
			roomsToUnseal.UnionWith(reachableRooms);
		}

		// TODO: slight time delay?
		foreach (RoomController room in roomsToUnseal)
		{
			room.SealRoom(false);
		}
	}

	public void OnLastAvatarDeath()
	{
		if (m_inputManager != null)
		{
			m_inputManager.DisableJoining();
		}
		Simulation.Schedule<GameOver>(3.0f); // TODO: time via animation event?
	}

	public void OnGameOver() => StartCoroutine(ActivateMenuCoroutine(m_gameOverUI, true));

	public void DeleteSaveAndQuit()
	{
		SaveHelpers.Delete();
		ZonesFinishedCount = 0;
		m_secretsFoundBitmask.SetAll(false);
		Quit(true);
	}

	public void Retry(bool noInventoryClear)
	{
		if (ConsoleCommands.RegenerateDisabled)
		{
			foreach (RoomController room in m_startRoom.WithDescendants)
			{
				room.SealRoom(false);
			}
			if (m_pauseUI.isActiveAndEnabled)
			{
				TogglePause();
			}
			foreach (AvatarController avatar in m_avatars)
			{
				avatar.Respawn(!noInventoryClear && !Victory, true);
			}
			Simulation.Schedule<DebugRespawn>();
			StartCoroutine(ActivateMenuCoroutine(m_gameOverUI, false));
			if (m_inputManager != null)
			{
				m_inputManager.EnableJoining();
			}
			return;
		}

		StartCoroutine(FadeCoroutine(true, SceneManager.GetActiveScene().name, !noInventoryClear, noInventoryClear));
	}

	public void LoadScene(string name) => StartCoroutine(FadeCoroutine(true, name));

	private IEnumerator LoadSceneCoroutine(string name, bool save, bool noInventoryClear)
	{
		Scene sceneCur = SceneManager.GetActiveScene();
		if (!IsSceneLoad && sceneCur.name != name)
		{
			SceneIndexPrev = sceneCur.buildIndex;
		}

		if (save)
		{
			Save();
		}

		if (Time.timeScale == 0.0f)
		{
			TogglePause();
		}
		yield return StartCoroutine(ActivateMenuCoroutine(m_gameOverUI, false));

		IsSceneLoad = true;

		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Respawn(!noInventoryClear && !Victory && ZonesFinishedCount < sceneCur.buildIndex, true);
		}

		SceneManager.LoadScene(name);
	}

	public void OnVictory()
	{
		if (Victory) // although OnVictory() shouldn't ever be called multiple times, we don't want to trigger effects in zones where Victory is set initially
		{
			GetComponent<MusicManager>().FadeOut(2.0f); // TODO: parameterize?
			return;
		}

		Victory = true;
		ZonesFinishedCount = System.Math.Max(ZonesFinishedCount, SceneManager.GetActiveScene().buildIndex);
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.OnVictory();
		}
		m_timerUI.gameObject.SetActive(false);
		m_nextWaveTime = -1.0f;
		StopAllCoroutines(); // TODO: continue spawning waves?

		GetComponent<MusicManager>().Play(m_victoryAudio);

		// TODO: roll credits / etc.?
	}

	public void Quit(bool isSaveDeletion)
	{
		if (!isSaveDeletion)
		{
			if (m_avatars.Count > 1)
			{
				OnPlayerLeft(m_avatars.Last().GetComponent<PlayerInput>());
				return;
			}

			if (SceneManager.GetActiveScene().buildIndex == 0) // to prevent save-quitting from Tutorial (see Save() exception for creating saves outside the Entryway) // TODO: replace Tutorial Quit button w/ Entryway button once Tutorial has been completed?
			{
				m_avatars.First().DetachAll(); // to save even items being held
				Save(); // TODO: prompt player?
			}
		}

#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#endif
		Application.Quit();
	}

	public void UpgradeActivate(InteractUpgrade.Type type, bool active, int index)
	{
		ref int upgradeCount = ref m_upgradeCounts[(int)type];
		upgradeCount += active ? 1 : -1;
		Debug.Assert(upgradeCount >= 0);

		if (index >= 0)
		{
			m_upgradesActiveBitmask.Set(index, active);
		}

		foreach (AvatarController avatar in m_avatars)
		{
			ApplyUpgrades(avatar);
		}
	}


	// callbacks for DialogueController.SendMessages()/SendMessage(Line.Reply.m_preconditionName, Line.Reply)
	public void EnemyTypeHasSpawned(DialogueController.Line.Reply reply) => reply.m_deactivated = !EnemyTypeHasSpawned(reply.m_userdata);
	public void SecretFound(DialogueController.Line.Reply reply) => reply.m_deactivated = !SecretFound(reply.m_userdata);


#if DEBUG
	public void DebugSpawnEnemy(int typeIndex)
	{
		if (typeIndex >= m_enemyPrefabs.Length)
		{
			return; // NOTE that we don't complain since this is triggered from user input
		}

		SpawnEnemy(m_enemyPrefabs[typeIndex].m_object);
		++m_enemySpawnCounts[typeIndex];
	}

	public void DebugKillAllEnemies(bool partial)
	{
		foreach (AIController enemy in m_enemies)
		{
			enemy.GetComponent<Health>().Decrement(gameObject, null, partial ? 0.1f : float.MaxValue, Health.DamageType.Generic);
		}
	}

	public void DebugResetWaves() => m_waveWeight = m_waveStartWeight;

	public static void DebugToggleAllUnlocks()
	{
		ZonesFinishedCount = ZonesFinishedCount == m_zoneCount ? 0 : m_zoneCount;
		m_secretsFoundBitmask.SetAll(!m_secretsFoundBitmask.Get(0));
	}

	public
#else
	private
#endif
		void NpcsRandomize()
	{
		m_npcs = m_npcAttitudes.RandomWeightedOrder().Zip(m_npcRoles.RandomWeightedOrder(), (a, b) => new[] { a, b }).Select(dialogue => new NpcInfo { m_color = Utility.ColorRandom(Color.black, Color.white, false), m_dialogues = dialogue }).ToArray();
		for (int i = 0; i < NpcsTotal; ++i) // TODO: replace w/ ColorRandom() colorsToAvoid param?
		{
			NpcInfo info1 = m_npcs[i];
			for (int j = i + 1; j < NpcsTotal; ++j)
			{
				NpcInfo info2 = m_npcs[j];
				if (info1.m_color.ColorsSimilar(info2.m_color))
				{
					// TODO: ensure flipping does not result in conflict w/ any previous color?
					info2.m_color = info2.m_color.ColorFlipComponent(Random.Range(0, 3), Color.black, Color.white);
				}
			}
		}
	}


	private int AddRoomsForNodes(LayoutGenerator.Node[] nodes, int roomCount, ref int orderedLockIdx)
	{
		List<LayoutGenerator.Node> nodesShuffled = nodes.OrderBy(node => Random.value).ToList();
		Queue<List<LayoutGenerator.Node>> nodesSplit = new();
		for (int i = 0; i < nodesShuffled.Count; )
		{
			List<LayoutGenerator.Node> newList = new();
			int numDoors = 0;
			int doorsMax = 6 - (m_startRoom == null ? 0 : 1); // TODO: determine based on room prefab
			bool isDoor = false;
			do
			{
				LayoutGenerator.Node node = nodesShuffled[i];
				static bool isExtraRoomType(LayoutGenerator.Node.Type t) => t == LayoutGenerator.Node.Type.RoomVertical || t == LayoutGenerator.Node.Type.RoomDown || t == LayoutGenerator.Node.Type.RoomUp || t == LayoutGenerator.Node.Type.RoomHorizontal || t == LayoutGenerator.Node.Type.RoomSecret || t == LayoutGenerator.Node.Type.RoomIndefinite || t == LayoutGenerator.Node.Type.RoomIndefiniteCorrect; // TODO: move into LayoutGenerator?
				if ((node.m_type == LayoutGenerator.Node.Type.Room && newList.Count > 0) || (isExtraRoomType(node.m_type) && newList.Exists(newNode => isExtraRoomType(newNode.m_type))))
				{
					break; // start new room
				}
				newList.Add(node);
				isDoor = node.m_type == LayoutGenerator.Node.Type.Lock || node.m_type == LayoutGenerator.Node.Type.LockOrdered || node.m_type == LayoutGenerator.Node.Type.GateBreakable || node.m_type == LayoutGenerator.Node.Type.Secret; // TODO: function?
				if (isDoor)
				{
					++numDoors;
				}
				++i;
			} while ((!isDoor || numDoors < doorsMax) && i < nodesShuffled.Count);

			nodesSplit.Enqueue(newList);
		}

		while (nodesSplit.TryDequeue(out List<LayoutGenerator.Node> nodesList))
		{
			if (nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.Entrance))
			{
				Assert.IsNull(m_startRoom);
				m_startRoom = Instantiate(m_entryRoomPrefabs.RandomWeighted()).GetComponent<RoomController>();
				m_startRoom.SetNodes(nodesList.ToArray());
				++roomCount;
				if (m_startRoom.transform.position.y >= 0.0f)
				{
					m_ctGroupOverview.AddMember(m_startRoom.m_backdrop.transform, 1.0f, 0.0f);
				}
				Debug.Assert(SpecialRooms == null);
				if (m_specialRoomCount > 0)
				{
					SpecialRooms = new RoomController[m_specialRoomCount];
				}
			}
			else
			{
				// find room to spawn from
				RoomController spawnRoom = null;
				foreach (LayoutGenerator.Node node in nodesList)
				{
					spawnRoom = node.TightCoupleParent?.m_room;
					if (spawnRoom != null)
					{
						break;
					}
				}
				if (spawnRoom == null)
				{
					// "I come back tomorrow."
					Assert.IsTrue(nodesSplit.Count > 0);
					nodesSplit.Enqueue(nodesList);
					continue;
				}

				// try spawning prefabs in random order
				RoomController childRoom = null;
				WeightedObject<RoomController>[] prefabsOrdered = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.Boss) ? m_bossRoomPrefabs : m_roomPrefabs;
				Vector2[] allowedDirections = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical) ? new[] { Vector2.down, Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomDown) ? new[] { Vector2.down } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomUp) ? new[] { Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) ? new[] { Vector2.left, Vector2.right } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret || node.m_type == LayoutGenerator.Node.Type.RoomIndefinite || node.m_type == LayoutGenerator.Node.Type.RoomIndefiniteCorrect) ? new[] { Vector2.left, Vector2.right, Vector2.down } : null;
				foreach (RoomController roomPrefab in prefabsOrdered.RandomWeightedOrder())
				{
					childRoom = spawnRoom.SpawnChildRoom(roomPrefab, nodesList.ToArray(), allowedDirections, ref orderedLockIdx); // TODO: bias RootCoupling child nodes toward existing leaf rooms?
					if (childRoom != null)
					{
						++roomCount;
						if (childRoom.transform.position.y >= 0.0f)
						{
							m_ctGroupOverview.AddMember(childRoom.m_backdrop.transform, 1.0f, 0.0f);
						}
						if (m_specialRoomCount > 0)
						{
							if (SpecialRooms.Any(room => room == null))
							{
								// fill random empty slot
								IEnumerable<int> emptyIndices = Enumerable.Range(0, m_specialRoomCount).Where(i => SpecialRooms[i] == null);
								SpecialRooms[emptyIndices.Random()] = childRoom;
							}
							else
							{
								// give each slot an even chance to be swapped
								for (int i = 0; i < m_specialRoomCount; ++i)
								{
									if (Random.value <= 1.0f / roomCount) // NOTE the 1/n chance to give each room an equal probability of final selection regardless of order
									{
										SpecialRooms[i] = childRoom;
										break; // to prevent SpecialRooms[] overlaps // TODO: parameterize? verify that this doesn't tilt the odds based on location w/i SpecialRooms[]?
									}
								}
							}
						}
						break;
					}
				}
				if (childRoom == null)
				{
					return 0;
				}
			}
		}
		return roomCount;
	}

	public void EnterAtDoor(params KinematicCharacter[] characters)
	{
		// find closest door prioritized by previous scene
		InteractScene[] doors = FindObjectsOfType<InteractScene>();
		InteractScene door = SceneIndexPrev < 0 ? null : doors.Where(interact => interact.DestinationIndex == SceneIndexPrev).OrderBy(interact => interact.transform.position.sqrMagnitude).FirstOrDefault();
		if (door == null)
		{
			door = doors.First(interact => Vector2.Distance(interact.transform.position, Vector2.zero) < 1.0f); // TODO: remove assumption that there will be a door at the origin?
		}

		// place characters(s) and aim(s)
		bool includesAvatar = false;
		foreach (KinematicCharacter character in characters)
		{
			character.Teleport(door.transform.position + (Vector3)character.gameObject.OriginToCenterY());
			if (character is AvatarController avatar)
			{
				includesAvatar = true;
				avatar.m_aimObject.transform.position = character.transform.position;
			}
			if (door.m_entryVFX.Length > 0)
			{
				Instantiate(door.m_entryVFX.RandomWeighted(), character.transform);
			}
		}

		// update shadow casting
		if (includesAvatar)
		{
			door.GetComponentInParent<RoomController>().LinkRecursive();
		}

		// animate door
		door.GetComponent<Animator>().SetTrigger("activate");
	}

	private void Save()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0 && SaveHelpers.Exists()) // NOTE that we have to initially create saves from the tutorial scene in order to avoid an infinite loop from Start() tutorial-load check // TODO: don't assume only first scene stores contents?
		{
			return;
		}

		using SaveWriter saveFile = new();

		saveFile.Write(m_npcs, npc =>
		{
			saveFile.Write(npc.m_dialogues, dialogue =>
			{
				bool dialogueCheckFunc(WeightedObject<Dialogue> dialogueWeighted) => dialogue == dialogueWeighted.m_object;
				int attitudeIdx = System.Array.FindIndex(m_npcAttitudes, dialogueCheckFunc);
				saveFile.Write(attitudeIdx >= 0 ? 0 : 1);
				saveFile.Write(attitudeIdx >= 0 ? attitudeIdx : System.Array.FindIndex(m_npcRoles, dialogueCheckFunc));
				saveFile.Write(dialogue.m_dialogue, option => saveFile.Write(option.m_weight));
			});
			saveFile.Write(npc.m_color);
		});

		saveFile.Write(MerchantAcquiredCounts, saveFile.Write);
		saveFile.Write(MerchantMaterials);

		saveFile.Write(m_enemySpawnCounts, saveFile.Write);

		saveFile.Write(ZonesFinishedCount);

		saveFile.Write(NarrowPathColors, color => saveFile.Write(color));

		int[] secretsFoundArray = new int[1]; // TODO: avoid limiting to a single int?
		m_secretsFoundBitmask.CopyTo(secretsFoundArray, 0);
		saveFile.Write(secretsFoundArray.First());

		int[] upgradesActiveArray = new int[1]; // TODO: avoid limiting to a single int?
		m_upgradesActiveBitmask.CopyTo(upgradesActiveArray, 0);
		saveFile.Write(upgradesActiveArray.First());
		saveFile.Write(m_upgradeCounts, saveFile.Write);

		GameObject[] savableObjs = m_savableTags.SelectMany(tag => GameObject.FindGameObjectsWithTag(tag)).Where(obj => obj.scene == SceneManager.GetActiveScene()).ToArray();
		saveFile.Write(savableObjs, obj => ISavable.Save(saveFile, obj.GetComponent<ISavable>()));
	}

	private bool Load()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0) // TODO: don't assume only first scene stores contents?
		{
			return SaveHelpers.Exists();
		}

		try
		{
			using SaveReader saveFile = new();
			if (!saveFile.IsOpen)
			{
				return false;
			}

			m_npcs = saveFile.ReadArray(() =>
			{
				Dialogue[] dialogue = saveFile.ReadArray(() =>
				{
					Dialogue dialogueTmp = (saveFile.ReadInt32() == 0 ? m_npcAttitudes : m_npcRoles)[saveFile.ReadInt32()].m_object;
					int idxItr = 0;
					saveFile.ReadArray(() =>
					{
						// NOTE the logic to preserve weights edited outside saved levels when re-entering a saved level, treating zero as un-edited and assuming non-zero weights drop lower over time // TODO: genericize?
						float savedWeight = saveFile.ReadSingle();
						ref float liveWeight = ref dialogueTmp.m_dialogue[idxItr++].m_weight;
						if (liveWeight == 0.0f || (savedWeight < liveWeight && savedWeight != 0.0f))
						{
							liveWeight = savedWeight;
						}
						return liveWeight;
					});
					return dialogueTmp;
				});
				return new NpcInfo { m_color = saveFile.ReadColor(), m_dialogues = dialogue };
			});

			// NOTE the somewhat awkward/not-fully-correct handling for entering a loaded scene after having incremented merchant numbers
			int i = 0;
			MerchantAcquiredCounts = MerchantAcquiredCounts == null ? saveFile.ReadArray(saveFile.ReadInt32) : saveFile.ReadArray(() =>
			{
				MerchantAcquiredCounts[i] = System.Math.Max(MerchantAcquiredCounts[i], saveFile.ReadInt32());
				return MerchantAcquiredCounts[i++];
			});
			MerchantMaterials = System.Math.Max(MerchantMaterials, saveFile.ReadInt32()); // NOTE that this is only workable due to never starting in a non-saved scene outside of debugging

			saveFile.Read(out int[] spawnCountsPrev, saveFile.ReadInt32);
			m_enemySpawnCounts = m_enemySpawnCounts == null ? spawnCountsPrev : m_enemySpawnCounts.Zip(spawnCountsPrev, (a, b) => System.Math.Max(a, b)).ToArray(); // TODO: don't assume array length will always match? guarantee accurate counts even if loading/quitting directly to/from non-saved scenes?

			ZonesFinishedCount = System.Math.Max(ZonesFinishedCount, saveFile.ReadInt32()); // NOTE the max() to somewhat handle debug loading directly into non-saved scenes, incrementing ZonesFinishedCount, and then loading a saved scene

			NarrowPathColors = saveFile.ReadArray(() => saveFile.ReadColor());

			m_secretsFoundBitmask.Or(new(new[] { saveFile.ReadInt32() })); // NOTE the OR to handle debug loading directly into non-saved scenes, editing m_secretsFoundBitmask, and then loading a saved scene

			// NOTE that we purposely overwrite any changes from non-saved scenes since intra-run upgrades are temporary
			m_upgradesActiveBitmask = new(new[] { saveFile.ReadInt32() });
			saveFile.Read(out m_upgradeCounts, saveFile.ReadInt32);

			saveFile.ReadArray(() => ISavable.Load(saveFile));
		}
		catch (System.Exception e)
		{
			Debug.LogError("Invalid save file: " + e.Message);
			// NOTE that we still return true since the save exists, even though it's apparently corrupted
		}

		return true;
	}

	private void ApplyUpgrades(AvatarController avatar)
	{
		GameObject avatarObjOrig = m_inputManager.playerPrefab;

		// health
		Health health = avatar.GetComponent<Health>();
		health.SetMax(avatarObjOrig.GetComponent<Health>().GetMax() + m_upgradeCounts[(int)InteractUpgrade.Type.Health]);

		// lighting
		// TODO: affect non-avatar lights?
		Light2D light = avatar.GetComponent<Light2D>();
		Light2D lightOrig = avatarObjOrig.GetComponent<Light2D>();
		int lightingUpgradeCount = m_upgradeCounts[(int)InteractUpgrade.Type.Lighting];
		light.pointLightOuterRadius = (lightingUpgradeCount + 1) * lightOrig.pointLightOuterRadius;
		light.NonpublicSetterWorkaround("m_NormalMapDistance", lightOrig.normalMapDistance + lightingUpgradeCount);

		// damage
		avatar.m_damageScalar = avatarObjOrig.GetComponent<KinematicCharacter>().m_damageScalar + m_upgradeCounts[(int)InteractUpgrade.Type.Damage];
	}

	private void StartWaves()
	{
		if (m_waveEscalationMax <= 0.0f || Random.value > 0.5f) // TODO: parameterize?
		{
			m_nextWaveTime = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
		}
		StartCoroutine(SpawnWavesCoroutine());
		if (m_waveEscalationMax > 0.0f)
		{
			StartCoroutine(TimerCoroutine());
		}
	}

	private IEnumerator SpawnWavesCoroutine()
	{
		WaitUntil waitUntilUnpaused = new(() => !ConsoleCommands.TimerPaused);

		if (m_waveEscalationMax > 0.0f)
		{
			// enable timer image/animation
			m_timerUI.gameObject.SetActive(true);
			if (m_nextWaveTime > Time.time)
			{
				m_timerAnimator.speed = 1.0f / (m_nextWaveTime - Time.time);
			}
		}

		while (m_nextWaveTime >= 0.0f)
		{
			yield return ConsoleCommands.TimerPaused ? waitUntilUnpaused : new WaitForSeconds(m_nextWaveTime - Time.time);
			if (ConsoleCommands.TimerPaused)
			{
				continue;
			}

			SpawnEnemyWave();
			float waveSeconds = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
			m_nextWaveTime = Time.time + waveSeconds;
			if (m_timerAnimator.isActiveAndEnabled)
			{
				m_timerAnimator.SetTrigger("reset");
				m_timerAnimator.speed = 1.0f / waveSeconds;
			}
		}
	}

#if DEBUG
	public
#else
	private
#endif
		void SpawnEnemyWave() => StartCoroutine(SpawnEnemyWaveCoroutine()); // TODO: ensure the previous wave is over first?

	private IEnumerator SpawnEnemyWaveCoroutine()
	{
		m_waveSpawningInProgress = true;

		// seal room(s) if scene demands it
		RoomController[] sealedRooms = m_waveSealing ? m_avatars.Select(avatar => RoomFromPosition(avatar.transform.position)).ToArray() : null;
		if (m_waveSealing)
		{
			foreach (RoomController room in sealedRooms)
			{
				room.SealRoom(true);
			}
			m_enemiesInWave.Clear(); // NOTE that any that remain able to reach an avatar will re-add themselves via AIController.NavigateTowardTarget()-->EnemyAddToWave(), and due to m_waveSpawningInProgress, ActiveEnemiesRemain() won't hiccup in the meantime
		}

		m_waveWeight += Random.Range(m_waveEscalationMin, m_waveEscalationMax); // TODO: exponential/logistic escalation?

		// TODO: poke m_musicManager?

		// determine enemy types allowed for this wave
		WeightedObject<AIController>[] optionsInitial = m_enemyPrefabs.Where(weightedObj => weightedObj.m_object.m_difficulty <= m_waveWeight).ToArray();
		float restrictPct = 1.0f - Mathf.Pow(Random.value, 2.0f); // NOTE that we bias the distribution toward more rather than less restriction
		WeightedObject<AIController>[] options = optionsInitial.Where(weightedObj => Random.value > restrictPct).ToArray();
		if (options.Length <= 0)
		{
			options = new[] { optionsInitial.Random() };
		}

		// sequentially spawn enemies
		m_waveWeightRemaining = m_waveWeight;
		for (int waveEnemyCount = 0; waveEnemyCount < m_waveEnemyCountMax && m_waveWeightRemaining > 0.0f; ++waveEnemyCount)
		{
			options = options.Where(weightedObj => weightedObj.m_object.m_difficulty <= m_waveWeightRemaining).ToArray(); // NOTE that since m_waveWeightRemaining never increases, it is safe to assume that all previously excluded options are still excluded
			if (options.Length <= 0)
			{
				break;
			}
			int idx = options.RandomWeightedIndex();
			AIController enemyPrefab = options[idx].m_object; // TODO: if no items in room, spawn enemy w/ included item
			SpawnEnemy(enemyPrefab);
			++m_enemySpawnCounts[idx];

			m_waveWeightRemaining -= enemyPrefab.m_difficulty;

			yield return new WaitForSeconds(Random.Range(m_waveEnemyDelayMin, m_waveEnemyDelayMax));
		}

		m_waveSpawningInProgress = false;

		// unseal rooms if the last enemy was killed immediately
		if (m_waveSealing && m_enemiesInWave.Count == 0 && !m_bossRoomSealed)
		{
			foreach (RoomController room in sealedRooms)
			{
				room.SealRoom(false);
			}
		}
	}

	private void SpawnEnemy(AIController enemyPrefab)
	{
		KinematicCharacter target = m_avatars.Count <= 0 ? AiTargets.FirstOrDefault(t => t is AIController ai && ai.m_friendly) : m_avatars.Random();
		RoomController spawnRoom = target == null ? m_startRoom.WithDescendants.Random() : m_waveSealing ? RoomFromPosition(target.transform.position) : RandomReachableRoom(enemyPrefab, target.gameObject, false);
		AIController enemy = Instantiate(enemyPrefab, spawnRoom.SpawnPointRandom(), Quaternion.identity);
		EnemyAdd(enemy);
		if (m_waveSealing) // NOTE that for non-sealed zones we don't invoke EnemyAddToWave() directly anymore, since we aren't guaranteed to be within reach of the avatar(s) - instead it will occur through AIController.NavigateTowardTarget(); however, we still need instantly active enemies in sealed zones to avoid immediately un-sealing during single-enemy waves
		{
			EnemyAddToWave(enemy);
		}
	}

	private void EnemyAddInternal(List<AIController> list, AIController enemy)
	{
		if (!list.Contains(enemy))
		{
			list.Add(enemy);
		}
	}

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		AIController ai = evt.m_object.GetComponent<AIController>();
		if (ai == null)
		{
			return;
		}

		m_enemies.Remove(ai);
		m_enemiesInWave.Remove(ai);
		m_npcsInstantiated.Remove(ai);

		if (m_waveSealing && !ActiveEnemiesRemain() && !m_bossRoomSealed)
		{
			// TODO: slight time delay?
			foreach (RoomController room in m_avatars.Select(avatar => RoomFromPosition(avatar.transform.position)))
			{
				room.SealRoom(false);
			}
		}
	}

	// TODO: start scene load in background during fade?
	private IEnumerator FadeCoroutine(bool fadeOut, string nextSceneName = null, bool save = true, bool noInventoryClear = false)
	{
		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause(Time.timeScale > 0.0f);
		}

		GetComponent<MusicManager>().FadeOut(m_fadeSeconds);

		Graphic[] graphics = m_loadingScreen.GetComponentsInChildren<Graphic>();
		float targetAlpha = fadeOut ? 1.0f : 0.0f;
		m_loadingScreen.SetActive(true);

		Color colorTmp;
		float[] alphaVels = new float[graphics.Length];
		while (graphics.Any(g => !g.color.a.FloatEqual(targetAlpha, 0.05f)))
		{
			int i = 0;
			foreach (Graphic g in graphics)
			{
				colorTmp = g.color;
				colorTmp.a = Mathf.SmoothDamp(g.color.a, targetAlpha, ref alphaVels[i], m_fadeSeconds);
				g.color = colorTmp;
				++i;
			}

			yield return null;
		}

		foreach (Graphic g in graphics)
		{
			colorTmp = g.color;
			colorTmp.a = targetAlpha;
			g.color = colorTmp;
		}

		if (!fadeOut)
		{
			m_loadingScreen.SetActive(false);
		}

		if (!string.IsNullOrEmpty(nextSceneName))
		{
			StartCoroutine(LoadSceneCoroutine(nextSceneName, save, noInventoryClear));
		}
	}

	private IEnumerator TimerCoroutine()
	{
		WaitForSeconds waitTime = new(1.0f);
		WaitUntil waitUntilUnpaused = new(() => !ConsoleCommands.TimerPaused);
		AudioSource source = GetComponent<AudioSource>();

		while (m_nextWaveTime >= 0.0f)
		{
			yield return ConsoleCommands.TimerPaused ? waitUntilUnpaused : waitTime; // NOTE that we currently don't care whether the UI timer is precise within partial seconds

			float secondsRemaining = m_nextWaveTime - Time.time;
			Color color = ActiveEnemiesRemain() ? Color.red : Color.white;
			foreach (Graphic g in m_timerUI.GetComponentsInChildren<Graphic>()) // TODO: cache for efficiency? move to wave start/end?
			{
				g.color = color;
			}

			if (secondsRemaining >= 1.0f && secondsRemaining <= m_waveWeight + 1.0f)
			{
				source.PlayOneShot(m_timerWarnSFX.RandomWeighted());
			}
		}
	}

	private IEnumerator ActivateMenuCoroutine(Canvas menu, bool active)
	{
		menu.gameObject.SetActive(active);

		yield return null; // delay one frame to mitigate input double-processing

		GameObject activeMenu = active ? menu.gameObject : m_gameOverUI.gameObject.activeSelf ? m_gameOverUI.gameObject : m_pauseUI.gameObject.activeSelf ? m_pauseUI.gameObject : m_dialogueController.m_replyMenu.gameObject.activeInHierarchy ? m_dialogueController.m_replyMenu.gameObject : null; // TODO: account for other UI instances?
		if (activeMenu != null)
		{
			UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(activeMenu.GetComponentInChildren<Button>().gameObject);
		}
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Controls.SwitchCurrentActionMap(active || m_pauseUI.gameObject.activeSelf || m_gameOverUI.gameObject.activeSelf ? "UI" : "Avatar"); // TODO: account for other UI instances?
		}

		yield return null; // delay one frame to mitigate input double-processing

		// TODO: move into DialogueController?
		if (m_dialogueController.IsPlaying && m_dialogueController.Character != null)
		{
			AvatarController avatar = m_dialogueController.Character as AvatarController;
			if (avatar != null)
			{
				avatar.ControlsUI.Enable();
			}
		}
	}
}
