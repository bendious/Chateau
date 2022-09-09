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

	public CinemachineVirtualCamera m_virtualCamera;
	public CinemachineTargetGroup m_cameraTargetGroup;

	public DialogueController m_dialogueController;

	public WeightedObject<GameObject>[] m_entryRoomPrefabs;
	public WeightedObject<GameObject>[] m_roomPrefabs;
	public WeightedObject<GameObject>[] m_bossRoomPrefabs;
	public WeightedObject<GameObject>[] m_gatePrefabs;
	public WeightedObject<GameObject>[] m_lockPrefabs;
	public WeightedObject<GameObject>[] m_keyPrefabs;
	public WeightedObject<GameObject>[] m_cutbackPrefabs;
	public WeightedObject<RoomType>[] m_roomTypes;
	public WeightedObject<RoomType>[] m_roomTypesSecret;
	public WeightedObject<GameObject>[] m_enemyPrefabs;
	[SerializeField] private WeightedObject<NpcDialogue>[] m_npcRoles;
	[SerializeField] private WeightedObject<NpcDialogue>[] m_npcAttitudes;
	public GameObject[] m_doorInteractPrefabs;
	[SerializeField] private GameObject[] m_zoneFinishedIndicators;
	[SerializeField] private GameObject[] m_upgradeIndicators;

	[SerializeField] private int m_specialRoomCount;
	public bool m_allowHiddenDestructibles = true;
	public bool m_allowCutbacks = true;
	[SerializeField] private bool m_waveSealing = false;
	public float m_zoneScalar = 1.0f;

	public float m_difficultyMin = 0.0f;
	public float m_difficultyMax = 0.0f;

	[SerializeField] private GameObject m_loadingScreen;
	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public MaterialSystem m_materialSystem;
	public SavableFactory m_savableFactory;
	public LightFlicker m_lightFlickerMaster;

	[SerializeField] private WeightedObject<AudioClip>[] m_timerWarnSFX;
	public AudioClip m_victoryAudio;

	public float m_waveSecondsMin = 45.0f;
	public float m_waveSecondsMax = 90.0f;
	public float m_waveStartWeight = 1.0f;
	public float m_waveEscalationMin = 0.0f;
	public float m_waveEscalationMax = 4.0f;
	public float m_waveEnemyDelayMin = 0.5f;
	public float m_waveEnemyDelayMax = 2.0f;


	[HideInInspector] public bool m_bossRoomSealed = false;


	public static bool IsSceneLoad { get; private set; }

	public static GameController Instance { get; private set; }

	public static NpcDialogue[] NpcDialogues(int index) => m_npcs[index].m_dialogues;
	public static Color NpcColor(int index) => m_npcs[index].m_color;

	public static int[] MerchantAcquiredCounts;
	public static int MerchantMaterials;

	public static int ZonesFinishedCount { get; private set; }

	public static bool SecretFound(int index) => m_secretsFoundBitmask[index];
	public static void SetSecretFound(int index) => m_secretsFoundBitmask.Set(index, true);


	public RoomController[] SpecialRooms { get; private set; }

	public Transform[] RoomBackdropsAboveGround => m_roomBackdropsAboveGroundInternal.Value;
	private readonly System.Lazy<Transform[]> m_roomBackdropsAboveGroundInternal = new(() => Instance.m_startRoom.BackdropsAboveGroundRecursive.ToArray(), false);

	public IEnumerable<KinematicCharacter> AiTargets => m_avatars.Select(avatar => (KinematicCharacter)avatar).Concat(m_enemiesActive); // TODO: efficiency?

	public bool Victory { get; private set; }


	[SerializeField] private string[] m_savableTags;


	public static int Seed => m_seed;
	private static int m_seed;

	public static int SceneIndexPrev { get; private set; } = -1;

	private RoomController m_startRoom;

	private struct NpcInfo
	{
		public Color m_color;
		public NpcDialogue[] m_dialogues;
	}
	private static NpcInfo[] m_npcs;

	private float m_waveWeight;
	private float m_nextWaveTime = 0.0f;
	private bool m_waveSpawningInProgress = false;

	private readonly List<AIController> m_enemiesActive = new();
	private static int[] m_enemySpawnCounts;

	private static readonly BitArray m_secretsFoundBitmask = new(sizeof(int) * 8); // TODO: avoid limiting to a single int?

	private static readonly BitArray m_upgradesActiveBitmask = new(sizeof(int) * 8); // TODO: avoid limiting to a single int?
	private static /*readonly*/ int[] m_upgradeCounts = new int[Utility.EnumNumTypes<InteractUpgrade.Type>() - 1]; // NOTE the -1 since InteractUpgrade.Type.None is excluded


	private void Awake()
	{
		Instance = this;

#if !FIXED_SEED
		m_seed = Random.Range(int.MinValue, int.MaxValue); // TODO: don't use Random to seed Random?
#endif
		Random.InitState(m_seed);

		// TODO: use Animator on persistent object?
		Image loadImage = m_loadingScreen.GetComponentsInChildren<Image>().Last()/*TODO*/;
		float alpha = Mathf.Abs((Time.realtimeSinceStartup % 1.0f) - 0.5f) * 2.0f;
		loadImage.color = new(loadImage.color.r, loadImage.color.g, loadImage.color.b, alpha);
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
		if (m_enemySpawnCounts == null)
		{
			m_enemySpawnCounts = new int[m_enemyPrefabs.Length]; // TODO: don't assume the same number/arrangement of enemies in each scene
		}

		int doorwayDepth = 0;
		int npcDepth = 0;
		m_startRoom.FinalizeRecursive(ref doorwayDepth, ref npcDepth);
		RoomController[] roomsHighToLow = m_startRoom.WithDescendants.OrderBy(room => -room.transform.position.y).ToArray();
		float roomWidthMin = roomsHighToLow.Min(room => room.Bounds.size.x);
		foreach (RoomController room in roomsHighToLow)
		{
			room.FinalizeTopDown(roomWidthMin);
		}

		if (m_avatars.Count > 0)
		{
			EnterAtDoor(m_avatars);
		}

		if (!saveExists)
		{
			// TODO: move earlier?
			const string tutorialSceneName = "Tutorial"; // TODO: un-hardcode?
			if (SceneManager.GetActiveScene().name != tutorialSceneName)
			{
				LoadScene(tutorialSceneName, false, false);
				return;
			}

			m_dialogueController.Play(new DialogueController.Line[] { new() { m_text = "Ah, welcome home." }, new() { m_text = "You've been out for quite a while, haven't you?" }, new() { m_text = "You're not going to claim grounds for outrage if a few... uninvited guests have shown up in the mean time, are you?", m_replies = new DialogueController.Line.Reply[] { new() { m_text = "Who are you, ya creep?" }, new() { m_text = "Who are you?" }, new() { m_text = "Who are you, sir?" } } }, new() { m_text = "An old friend." }, new() { m_text = "I'm not surprised you don't remember me. Is there anything you do remember, after all?" }, new() { m_text = "But don't worry about me; you have more pressing concerns at the moment, I believe." } });
		}

		m_loadingScreen.SetActive(false);

		IsSceneLoad = false;
	}

	private void OnDestroy()
	{
		IsSceneLoad = true;
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}

	private void Update()
	{
		// constrain camera
		// TODO: efficiency?
		AvatarController liveAvatar = m_avatars.FirstOrDefault(avatar => avatar.IsAlive);
		RoomController constraintRoom = liveAvatar == null ? null : RoomFromPosition(liveAvatar.transform.position);
		bool unconstrained = constraintRoom == null || m_avatars.Any(avatar => avatar.IsAlive && (avatar.IsLooking || RoomFromPosition(avatar.transform.position) != constraintRoom));
		m_virtualCamera.GetComponentInChildren<CinemachineConfiner2D>().m_BoundingShape2D = unconstrained ? null : constraintRoom.GetComponentInChildren<PolygonCollider2D>();

		Simulation.Tick();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via SendMessage() from PlayerInputManager component")]
	private void OnPlayerJoined(PlayerInput player)
	{
		// NOTE that we can place this here since OnPlayerJoined() is called even if the avatar object(s) is/are carried over from a previously loaded scene
		if (m_enemyPrefabs.Length > 0 && m_waveSecondsMin > 0.0f)
		{
			if (m_avatars.Count == 0)
			{
				if (Random.value > 0.5f)
				{
					m_nextWaveTime = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
				}
				StopAllCoroutines();
				StartCoroutine(SpawnWavesCoroutine());
				StartCoroutine(TimerCoroutine());
			}
		}
		else
		{
			m_timerUI.text = null;
			Victory = true;
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
			}

			// NOTE that we don't replace the whole m_Targets array in case a non-avatar object is also present
			AddCameraTargets(avatar.transform);

			isFirst = false;
		}

		if (m_startRoom != null)
		{
			EnterAtDoor(new[] { m_avatars.Last() });
		}
	}

	private void OnApplicationQuit() => IsSceneLoad = true;

	private void OnPlayerLeft(PlayerInput player)
	{
		Simulation.Schedule<ObjectDespawn>().m_object = player.gameObject;
		m_avatars.Remove(player.GetComponent<AvatarController>());
		// TODO: clean up camera targeting?
	}


	public void AddCameraTargets(params Transform[] transforms)
	{
		foreach (Transform tf in transforms)
		{
			m_cameraTargetGroup.RemoveMember(tf); // prevent duplication // TODO: necessary?
			m_cameraTargetGroup.AddMember(tf, 1.0f, 0.0f); // TODO: blend weight in? calculate/expose radius?
			// TODO: auto-remove on target destruction?
		}
	}

	public void RemoveCameraTargets(params Transform[] transforms)
	{
		foreach (Transform tf in transforms)
		{
			m_cameraTargetGroup.RemoveMember(tf); // TODO: blend weight out?
		}
	}

	public RoomController RoomFromPosition(Vector2 position) => m_startRoom.FromPosition(position);

	public List<Vector2> Pathfind(GameObject start, GameObject target, float extentY = -1.0f, float upwardMax = float.MaxValue, Vector2 offsetMag = default, RoomController.PathFlags flags = RoomController.PathFlags.ObstructionCheck)
	{
		RoomController startRoom = RoomFromPosition(start.transform.position); // TODO: use closest bbox point?
		return startRoom == null ? null : startRoom.PositionPath(start, target, flags, extentY, upwardMax, offsetMag);
	}

	public void TogglePause()
	{
		if (m_gameOverUI.gameObject.activeSelf)
		{
			return;
		}

		Time.timeScale = Time.timeScale == 0.0f ? 1.0f : 0.0f;
		ActivateMenu(m_pauseUI, !m_pauseUI.gameObject.activeSelf);
		// NOTE that if the avatar is ever visible while paused, we should disable its script here to avoid continuing to update facing
	}

	public void EnemyAdd(AIController enemy)
	{
		if (m_enemiesActive.Contains(enemy))
		{
			return;
		}
		m_enemiesActive.Add(enemy);
		// TODO: increment m_enemySpawnCounts[] if appropriate
	}

	public bool ActiveEnemiesRemain() => m_waveSpawningInProgress || m_enemiesActive.Count > 0;

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

		foreach (AIController enemy in m_enemiesActive)
		{
			RoomController room = RoomFromPosition(enemy.transform.position);
			if (reachableRooms.Contains(room))
			{
				continue;
			}

			unreachableEnemies.Add(enemy);
			roomsToUnseal.Add(room);
		}
		m_enemiesActive.RemoveAll(enemy => unreachableEnemies.Contains(enemy));

		if (m_enemiesActive.Count <= 0)
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
		GetComponent<PlayerInputManager>().DisableJoining();
		Simulation.Schedule<GameOver>(3.0f); // TODO: time via animation event?
	}

	public void OnGameOver() => ActivateMenu(m_gameOverUI, true);

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
			ActivateMenu(m_gameOverUI, false);
			GetComponent<PlayerInputManager>().EnableJoining();
			return;
		}

		LoadScene(SceneManager.GetActiveScene().name, !noInventoryClear, noInventoryClear);
	}

	public void LoadScene(string name) => LoadScene(name, true, false);

	public void LoadScene(string name, bool save, bool noInventoryClear)
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

		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause();
		}
		ActivateMenu(m_gameOverUI, false);

		IsSceneLoad = true;

		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Respawn(!noInventoryClear && !Victory && ZonesFinishedCount < sceneCur.buildIndex, true);
		}

		SceneManager.LoadScene(name);
	}

	public void OnVictory()
	{
		Victory = true;
		ZonesFinishedCount = System.Math.Max(ZonesFinishedCount, SceneManager.GetActiveScene().buildIndex);
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.OnVictory();
		}
		m_timerUI.text = null;
		m_nextWaveTime = -1.0f;
		StopAllCoroutines();

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

			m_avatars.First().DetachAll(); // to save even items being held
			Save(); // TODO: prompt player?
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

		m_upgradesActiveBitmask.Set(index, active);

		foreach (AvatarController avatar in m_avatars)
		{
			ApplyUpgrades(avatar);
		}
	}


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

	public void DebugKillAllEnemies()
	{
		foreach (AIController enemy in m_enemiesActive)
		{
			enemy.GetComponent<Health>().Die();
		}
	}

	public void DebugResetWaves() => m_waveWeight = m_waveStartWeight;

	public static void DebugToggleAllZones() => ZonesFinishedCount = ZonesFinishedCount == 3 ? 0 : 3; // TODO: remove hardcoding?

	public
#else
	private
#endif
		void NpcsRandomize() => m_npcs = m_npcAttitudes.RandomWeightedOrder().Zip(m_npcRoles.RandomWeightedOrder(), (a, b) => new[] { a, b }).Select(dialogue => new NpcInfo { m_color = Utility.ColorRandom(Color.black, Color.white, false), m_dialogues = dialogue }).ToArray(); // TODO: ensure good colors w/o repeats?


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
				static bool isExtraRoomType(LayoutGenerator.Node.Type t) => t == LayoutGenerator.Node.Type.RoomVertical || t == LayoutGenerator.Node.Type.RoomDown || t == LayoutGenerator.Node.Type.RoomUp || t == LayoutGenerator.Node.Type.RoomHorizontal || t == LayoutGenerator.Node.Type.RoomIndefinite; // TODO: move into LayoutGenerator?
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
				m_startRoom = Instantiate(m_entryRoomPrefabs.RandomWeighted(), transform).GetComponent<RoomController>();
				m_startRoom.SetNodes(nodesList.ToArray());
				++roomCount;
				Debug.Assert(SpecialRooms == null);
				SpecialRooms = Enumerable.Repeat(m_startRoom, m_specialRoomCount).ToArray();
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
				WeightedObject<GameObject>[] prefabsOrdered = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.Boss) ? m_bossRoomPrefabs : m_roomPrefabs;
				Vector2[] allowedDirections = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical) ? new[] { Vector2.down, Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomDown) ? new[] { Vector2.down } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomUp) ? new[] { Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) ? new[] { Vector2.left, Vector2.right } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret) ? new[] { Vector2.left, Vector2.right, Vector2.down } : null;
				foreach (GameObject roomPrefab in prefabsOrdered.RandomWeightedOrder())
				{
					childRoom = spawnRoom.SpawnChildRoom(roomPrefab, nodesList.ToArray(), allowedDirections, ref orderedLockIdx); // TODO: bias RootCoupling child nodes toward existing leaf rooms?
					if (childRoom != null)
					{
						++roomCount;
						if (m_specialRoomCount > 0)
						{
							for (int i = 0; i < SpecialRooms.Length; ++i)
							{
								if (Random.value <= 1.0f / roomCount) // NOTE the 1/n chance to give each room an equal probability of final selection regardless of order
								{
									SpecialRooms[i] = childRoom;
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

	private void EnterAtDoor(IEnumerable<AvatarController> avatars)
	{
		// find closest door prioritized by previous scene
		InteractScene[] doors = FindObjectsOfType<InteractScene>();
		InteractScene door = SceneIndexPrev < 0 ? null : doors.Where(interact => interact.DestinationIndex == SceneIndexPrev).OrderBy(interact => interact.transform.position.sqrMagnitude).FirstOrDefault();
		if (door == null)
		{
			door = doors.First(interact => Vector2.Distance(interact.transform.position, Vector2.zero) < 1.0f); // TODO: remove assumption that there will be a door at the origin?
		}

		// place avatar(s) and aim(s)
		foreach (AvatarController avatar in avatars)
		{
			avatar.Teleport(door.transform.position + (Vector3)avatar.gameObject.OriginToCenterY());
			avatar.m_aimObject.transform.position = avatar.transform.position;
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
				bool dialogueCheckFunc(WeightedObject<NpcDialogue> dialogueWeighted) => dialogue == dialogueWeighted.m_object;
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
				NpcDialogue[] dialogue = saveFile.ReadArray(() =>
				{
					NpcDialogue dialogueTmp = (saveFile.ReadInt32() == 0 ? m_npcAttitudes : m_npcRoles)[saveFile.ReadInt32()].m_object;
					int idxItr = 0;
					saveFile.ReadArray(() => dialogueTmp.m_dialogue[idxItr++].m_weight += saveFile.ReadSingle()); // NOTE the += to preserve weights edited outside saved levels when re-entering a saved level
					return dialogueTmp;
				});
				return new NpcInfo { m_color = saveFile.ReadColor(), m_dialogues = dialogue };
			});

			// NOTE the somewhat awkward handling for entering a loaded scene after having incremented merchant numbers // TODO: prevent merchant services before returning to Entryway?
			int i = 0;
			MerchantAcquiredCounts = MerchantAcquiredCounts == null ? saveFile.ReadArray(saveFile.ReadInt32) : saveFile.ReadArray(() =>
			{
				MerchantAcquiredCounts[i] = System.Math.Max(MerchantAcquiredCounts[i], saveFile.ReadInt32());
				return MerchantAcquiredCounts[i++];
			});
			MerchantMaterials = System.Math.Max(MerchantMaterials, saveFile.ReadInt32());

			saveFile.Read(out int[] spawnCountsPrev, saveFile.ReadInt32);
			m_enemySpawnCounts = m_enemySpawnCounts == null ? spawnCountsPrev : m_enemySpawnCounts.Zip(spawnCountsPrev, (a, b) => System.Math.Max(a, b)).ToArray(); // TODO: don't assume array length will always match? guarantee accurate counts even if loading/quitting directly to/from non-saved scenes?

			ZonesFinishedCount = System.Math.Max(ZonesFinishedCount, saveFile.ReadInt32()); // NOTE the max() to somewhat handle debug loading directly into non-saved scenes, incrementing ZonesFinishedCount, and then loading a saved scene

			m_secretsFoundBitmask.Or(new(new[] { saveFile.ReadInt32() })); // NOTE the OR to handle debug loading directly into non-saved scenes, editing m_secretsFoundBitmask, and then loading a saved scene

			m_upgradesActiveBitmask.Or(new(new[] { saveFile.ReadInt32() })); // NOTE the OR to handle debug loading directly into non-saved scenes, editing m_upgradesActiveBitmask, and then loading a saved scene // TODO: necessary?
			saveFile.Read(out m_upgradeCounts, saveFile.ReadInt32);

			saveFile.ReadArray(() => ISavable.Load(saveFile));
		}
		catch (System.Exception e)
		{
			Debug.LogError("Invalid save file: " + e.Message);
			// NOTE that we still return true since the save exists, even though it's apparently corrupted
		}

		// spawn progress indicator(s)
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

		return true;
	}

	private void ApplyUpgrades(AvatarController avatar)
	{
		GameObject avatarObjOrig = GetComponent<PlayerInputManager>().playerPrefab;

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

	private IEnumerator SpawnWavesCoroutine()
	{
		WaitUntil waitUntilUnpaused = new(() => !ConsoleCommands.TimerPaused);

		while (m_nextWaveTime >= 0.0f)
		{
			yield return ConsoleCommands.TimerPaused ? waitUntilUnpaused : new WaitForSeconds(m_nextWaveTime - Time.time);
			if (ConsoleCommands.TimerPaused)
			{
				continue;
			}

			SpawnEnemyWave();
			m_nextWaveTime = Time.time + Random.Range(m_waveSecondsMin, m_waveSecondsMax);
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
			m_enemiesActive.Clear(); // NOTE that any that remain able to reach an avatar will re-add themselves via EnemyAdd(), and due to m_waveSpawningInProgress, ActiveEnemiesRemain() won't hiccup in the meantime
		}

		m_waveWeight += Random.Range(m_waveEscalationMin, m_waveEscalationMax); // TODO: exponential/logistic escalation?

		// determine enemy types allowed for this wave
		WeightedObject<GameObject>[] optionsInitial = m_enemyPrefabs.Where(weightedObj => weightedObj.m_weight <= m_waveWeight).ToArray();
		float restrictPct = 1.0f - Mathf.Pow(Random.value, 2.0f); // NOTE that we bias the distribution toward more rather than less restriction
		WeightedObject<GameObject>[] options = optionsInitial.Where(weightedObj => Random.value > restrictPct).ToArray();
		if (options.Length <= 0)
		{
			options = new[] { optionsInitial.Random() };
		}

		// sequentially spawn enemies
		for (float weightRemaining = m_waveWeight; weightRemaining > 0.0f; )
		{
			options = options.Where(weightedObj => weightedObj.m_weight <= weightRemaining).ToArray(); // NOTE that since weightRemaining never increases, it is safe to assume that all previously excluded options are still excluded
			if (options.Length <= 0)
			{
				break;
			}
			int idx = Random.Range(0, options.Length);
			WeightedObject<GameObject> weightedEnemyPrefab = options[idx]; // TODO: if no items in room, spawn enemy w/ included item
			SpawnEnemy(weightedEnemyPrefab.m_object);
			++m_enemySpawnCounts[idx];

			weightRemaining -= weightedEnemyPrefab.m_weight;

			yield return new WaitForSeconds(Random.Range(m_waveEnemyDelayMin, m_waveEnemyDelayMax));
		}

		m_waveSpawningInProgress = false;

		// unseal rooms if the last enemy was killed immediately
		if (m_waveSealing && m_enemiesActive.Count == 0 && !m_bossRoomSealed)
		{
			foreach (RoomController room in sealedRooms)
			{
				room.SealRoom(false);
			}
		}
	}

	private void SpawnEnemy(GameObject enemyPrefab)
	{
		Vector3 spawnPos = RoomFromPosition(m_avatars.Random().transform.position).SpawnPointRandom();
		EnemyAdd(Instantiate(enemyPrefab, spawnPos, Quaternion.identity).GetComponent<AIController>());
	}

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		AIController ai = evt.m_object.GetComponent<AIController>();
		if (ai == null)
		{
			return;
		}

		m_enemiesActive.Remove(ai);

		if (m_waveSealing && !ActiveEnemiesRemain() && !m_bossRoomSealed)
		{
			// TODO: slight time delay?
			foreach (RoomController room in m_avatars.Select(avatar => RoomFromPosition(avatar.transform.position)))
			{
				room.SealRoom(false);
			}
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
			m_timerUI.text = System.TimeSpan.FromSeconds(secondsRemaining).ToString("m':'ss");
			m_timerUI.color = ActiveEnemiesRemain() ? Color.red : Color.green;

			if (secondsRemaining >= 1.0f && secondsRemaining <= m_waveWeight + 1.0f)
			{
				source.PlayOneShot(m_timerWarnSFX.RandomWeighted());
			}
		}
	}

	private void ActivateMenu(Canvas menu, bool active)
	{
		menu.gameObject.SetActive(active);
		if (active)
		{
			UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(menu.GetComponentInChildren<Button>().gameObject);
		}
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Controls.SwitchCurrentActionMap(m_pauseUI.gameObject.activeSelf || m_gameOverUI.gameObject.activeSelf || avatar.m_overlayCanvas.gameObject.activeSelf ? "UI" : "Avatar"); // TODO: account for other UI instances?
		}
	}
}
