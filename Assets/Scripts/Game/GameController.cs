//#define FIXED_SEED


using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
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

	public GameObject m_avatarPrefab;
	public WeightedObject<GameObject>[] m_entryRoomPrefabs;
	public WeightedObject<GameObject>[] m_roomPrefabs;
	public WeightedObject<GameObject>[] m_bossRoomPrefabs;
	public WeightedObject<GameObject>[] m_gatePrefabs;
	public WeightedObject<GameObject>[] m_lockPrefabs;
	public WeightedObject<GameObject>[] m_keyPrefabs;
	public WeightedObject<RoomType>[] m_roomTypes;
	public WeightedObject<GameObject>[] m_enemyPrefabs;
	[SerializeField] private WeightedObject<NpcDialogue>[] m_npcRoles;
	[SerializeField] private WeightedObject<NpcDialogue>[] m_npcAttitudes;
	public GameObject[] m_doorInteractPrefabs;

	public bool m_allowCutbacks = true;
	[SerializeField] private bool m_waveSealing = false;
	public float m_zoneScalar = 1.0f;

	[SerializeField] private GameObject m_loadingScreen;
	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public MaterialSystem m_materialSystem;
	public SavableFactory m_savableFactory;

	[SerializeField]
	private WeightedObject<AudioClip>[] m_timerWarnSFX;
	[SerializeField]
	private WeightedObject<AudioClip>[] m_ambientMusic;
	[SerializeField]
	private float m_musicDelayMin = 30.0f;
	[SerializeField]
	private float m_musicDelayMax = 120.0f;
	public AudioClip m_victoryAudio;

	public float m_waveSecondsMin = 45.0f;
	public float m_waveSecondsMax = 90.0f;
	public float m_waveStartWeight = 1.0f;
	public float m_waveEscalationMin = 0.0f;
	public float m_waveEscalationMax = 4.0f;
	public float m_waveEnemyDelayMin = 0.5f;
	public float m_waveEnemyDelayMax = 2.0f;

	[SerializeField] private string m_tutorialCompleteKey = "TutorialComplete";


	[HideInInspector]
	public bool m_bossRoomSealed = false;


	public static bool IsSceneLoad { get; private set; }

	public static GameController Instance { get; private set; }

	public static NpcDialogue[][] Npcs { get; private set; }
	public static int[] MerchantAcquiredCounts;
	public static int MerchantMaterials;

	public static int ZonesFinishedCount { get; private set; }


	public RoomController LootRoom { get; private set; }

	public Transform[] RoomBackdrops => m_roomBackdropsInternal.Value;
	private readonly System.Lazy<Transform[]> m_roomBackdropsInternal = new(() => Instance.m_startRoom.BackdropsRecursive, false);

	public bool Victory { get; private set; }


	[SerializeField]
	private string[] m_savableTags;


	public static int Seed => m_seed;
	private static int m_seed;

	private RoomController m_startRoom;

	private float m_waveWeight;
	private float m_nextWaveTime = 0.0f;
	private bool m_waveSpawningInProgress = false;

	private readonly List<EnemyController> m_enemies = new();
	private static int[] m_enemySpawnCounts;


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
		loadImage.color = new Color(loadImage.color.r, loadImage.color.g, loadImage.color.b, alpha);
	}

	private void Start()
	{
		ObjectDespawn.OnExecute += OnObjectDespawn;

		m_waveWeight = m_waveStartWeight;

		LayoutGenerator generator = new(new LayoutGenerator.Node(m_type));
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

		Load();

		// first-time initializations
		if (Npcs == null)
		{
			Npcs = m_npcAttitudes.RandomWeightedOrder().Zip(m_npcRoles.RandomWeightedOrder(), (a, b) => new NpcDialogue[] { a, b }).ToArray();
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

		if (m_avatars.Count > 0)
		{
			AnimateEntryDoor();
		}

		// TODO: system?
		if (!PlayerPrefs.HasKey(m_tutorialCompleteKey))
		{
			const string tutorialSceneName = "Tutorial"; // TODO: un-hardcode?
			if (SceneManager.GetActiveScene().name != tutorialSceneName)
			{
				LoadScene(tutorialSceneName, false, false);
				return;
			}

			m_dialogueController.Play(null, Color.black, new DialogueController.Line[] { new DialogueController.Line { m_text = "Ah, welcome home." }, new DialogueController.Line { m_text = "You've been out for quite a while, haven't you?" }, new DialogueController.Line { m_text = "You're not going to claim grounds for outrage if a few... uninvited guests have shown up in the mean time, are you?", m_replies = new DialogueController.Line.Reply[] { new DialogueController.Line.Reply { m_text = "Who are you, ya creep?" }, new DialogueController.Line.Reply { m_text = "Who are you?" }, new DialogueController.Line.Reply { m_text = "Who are you, sir?" } } }, new DialogueController.Line { m_text = "An old friend." }, new DialogueController.Line { m_text = "I'm not surprised you don't remember me. Is there anything you do remember, after all?" }, new DialogueController.Line { m_text = "But don't worry about me; you have more pressing concerns at the moment, I believe." } }, null, null);
		}

		m_loadingScreen.SetActive(false);

		IsSceneLoad = false;

		StartCoroutine(MusicCoroutine());
	}

	private void OnDestroy()
	{
		IsSceneLoad = true;
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}

	private void Update()
	{
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

		m_avatars.Add(player.GetComponent<AvatarController>());
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
						tf.anchorMin = new Vector2(1.0f, tf.anchorMin.y);
						tf.anchorMax = new Vector2(1.0f, tf.anchorMax.y);
						tf.pivot = new Vector2(1.0f, tf.pivot.y);
						tf.anchoredPosition *= new Vector2(-1.0f, 1.0f);
					}
				}
			}

			// NOTE that we don't replace the whole m_Targets array in case a non-avatar object is also present
			AddCameraTargets(avatar.m_aimObject.transform, avatar.transform);

			isFirst = false;
		}

		if (m_startRoom != null)
		{
			AnimateEntryDoor();
		}
	}

	private void OnApplicationQuit()
	{
		IsSceneLoad = true;
	}

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

	public RoomController RoomFromPosition(Vector2 position)
	{
		return m_startRoom.FromPosition(position);
	}

	public List<Vector2> Pathfind(Vector2 startPos, Vector2 targetPos, Vector2 offsetMag)
	{
		RoomController startRoom = RoomFromPosition(startPos);
		return startRoom.PositionPath(startPos, targetPos, offsetMag, RoomController.ObstructionCheck.Full);
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

	public void EnemyAdd(EnemyController enemy)
	{
		m_enemies.Add(enemy);
	}

	public bool EnemiesRemain()
	{
		return m_waveSpawningInProgress || m_enemies.Count > 0;
	}

	public bool EnemyTypeHasSpawned(int typeIndex)
	{
		return m_enemySpawnCounts[typeIndex] > 0;
	}

	public void RemoveUnreachableEnemies()
	{
		// TODO: don't assume we're locked into individual rooms?
		RoomController[] reachableRooms = m_avatars.Where(avatar => avatar.IsAlive).Select(avatar => RoomFromPosition(avatar.transform.position)).ToArray();

		foreach (EnemyController enemy in m_enemies)
		{
			if (reachableRooms.Contains(RoomFromPosition(enemy.transform.position)))
			{
				continue;
			}
			Simulation.Schedule<ObjectDespawn>().m_object = enemy.gameObject;
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
		PlayerPrefs.DeleteAll(); // TODO: separate setup/configuration from game data
		SaveHelpers.Delete();
		ZonesFinishedCount = 0;
		Quit(true);
	}

	public void Retry(bool noInventoryClear)
	{
		if (ConsoleCommands.RegenerateDisabled)
		{
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

	public void LoadScene(string name)
	{
		LoadScene(name, true, false);
	}

	public void LoadScene(string name, bool save, bool noInventoryClear)
	{
		if (save)
		{
			PlayerPrefs.SetInt(m_tutorialCompleteKey, 1);
			Save();
		}

		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause();
		}
		ActivateMenu(m_gameOverUI, false);

		IsSceneLoad = true;

		// prevent stale GameController asserts while reloading
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.gameObject.SetActive(false);
		}

		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Respawn(!noInventoryClear && !Victory, true);
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
		m_timerUI.text = "WIN!";
		m_nextWaveTime = -1.0f;
		StopAllCoroutines(); // TODO: allow ambient music to restart after victory music?

		// NOTE that we have to use a component that won't be playing any more audio rather than using PlayOneShot(), which leaves the music running even after level load
		AudioSource source = GetComponent<AudioSource>();
		source.clip = m_victoryAudio;
		source.Play();

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
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.GetComponent<Health>().Die();
		}
	}

	public void DebugResetWaves() => m_waveWeight = m_waveStartWeight;
#endif


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
				if ((node.m_type == LayoutGenerator.Node.Type.Room && newList.Count > 0) || ((node.m_type == LayoutGenerator.Node.Type.RoomVertical || node.m_type == LayoutGenerator.Node.Type.RoomDown || node.m_type == LayoutGenerator.Node.Type.RoomUp || node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) && newList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical || node.m_type == LayoutGenerator.Node.Type.RoomDown || node.m_type == LayoutGenerator.Node.Type.RoomUp || node.m_type == LayoutGenerator.Node.Type.RoomHorizontal)))
				{
					break; // start new room
				}
				newList.Add(node);
				isDoor = node.m_type == LayoutGenerator.Node.Type.Lock || node.m_type == LayoutGenerator.Node.Type.LockOrdered || node.m_type == LayoutGenerator.Node.Type.Secret; // TODO: function?
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
				Debug.Assert(LootRoom == null);
				LootRoom = m_startRoom;
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
				Vector2[] allowedDirections = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical) ? new Vector2[] { Vector2.down, Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomDown) ? new Vector2[] { Vector2.down } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomUp) ? new Vector2[] { Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) ? new Vector2[] { Vector2.left, Vector2.right } : null;
				foreach (GameObject roomPrefab in prefabsOrdered.RandomWeightedOrder())
				{
					childRoom = spawnRoom.SpawnChildRoom(roomPrefab, nodesList.ToArray(), allowedDirections, ref orderedLockIdx); // TODO: bias RootCoupling child nodes toward existing leaf rooms?
					if (childRoom != null)
					{
						++roomCount;
						if (Random.value <= 1.0f / roomCount) // NOTE the 1/n chance to give each room an equal probability of final selection regardless of order
						{
							LootRoom = childRoom;
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

	private void AnimateEntryDoor()
	{
		Animator doorAnimator = m_startRoom.GetComponentsInChildren<Animator>().First(animator => Vector2.Distance(animator.transform.position, Vector2.zero) < 1.0f); // TODO: more reliable way of determining entry door?
		doorAnimator.SetTrigger("activate");
	}

	private void Save()
	{
		PlayerPrefs.Save();

		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0) // TODO: don't assume only first scene stores contents?
		{
			return;
		}

		using SaveWriter saveFile = new();

		saveFile.Write(Npcs, npc => saveFile.Write(npc, dialogue =>
		{
			bool dialogueCheckFunc(WeightedObject<NpcDialogue> dialogueWeighted) => dialogue == dialogueWeighted.m_object;
			int attitudeIdx = System.Array.FindIndex(m_npcAttitudes, dialogueCheckFunc);
			saveFile.Write(attitudeIdx >= 0 ? 0 : 1);
			saveFile.Write(attitudeIdx >= 0 ? attitudeIdx : System.Array.FindIndex(m_npcRoles, dialogueCheckFunc));
			saveFile.Write(dialogue.m_dialogue, option => saveFile.Write(option.m_weight));
		}));

		saveFile.Write(MerchantAcquiredCounts, saveFile.Write);
		saveFile.Write(MerchantMaterials);

		saveFile.Write(m_enemySpawnCounts, saveFile.Write);

		saveFile.Write(ZonesFinishedCount);

		GameObject[] savableObjs = m_savableTags.SelectMany(tag => GameObject.FindGameObjectsWithTag(tag)).Where(obj => obj.scene == SceneManager.GetActiveScene()).ToArray();
		saveFile.Write(savableObjs, obj => ISavable.Save(saveFile, obj.GetComponent<ISavable>()));
	}

	private void Load()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0) // TODO: don't assume only first scene stores contents?
		{
			return;
		}

		try
		{
			using SaveReader saveFile = new();
			if (!saveFile.IsOpen)
			{
				return;
			}

			Npcs = saveFile.ReadArray(() => saveFile.ReadArray(() =>
			{
				NpcDialogue dialogueTmp = (saveFile.ReadInt32() == 0 ? m_npcAttitudes : m_npcRoles)[saveFile.ReadInt32()].m_object;
				int idxItr = 0;
				saveFile.ReadArray(() => dialogueTmp.m_dialogue[idxItr++].m_weight += saveFile.ReadSingle()); // NOTE the += to preserve weights edited outside saved levels when re-entering a saved level
				return dialogueTmp;
			}));

			MerchantAcquiredCounts = saveFile.ReadArray(saveFile.ReadInt32);
			saveFile.Read(out MerchantMaterials);

			saveFile.Read(out int[] spawnCountsPrev, saveFile.ReadInt32);
			m_enemySpawnCounts = m_enemySpawnCounts == null ? spawnCountsPrev : m_enemySpawnCounts.Zip(spawnCountsPrev, (a, b) => System.Math.Max(a, b)).ToArray(); // TODO: don't assume array length will always match? guarantee accurate counts even if loading/quitting directly to/from non-saved scenes?

			ZonesFinishedCount = System.Math.Max(ZonesFinishedCount, saveFile.ReadInt32()); // NOTE the max() to somewhat handle debug loading directly into non-saved scenes, incrementing ZonesFinishedCount, and then loading a saved scene

			saveFile.ReadArray(() => ISavable.Load(saveFile));
		}
		catch (System.Exception e)
		{
			Debug.LogError("Invalid save file: " + e.Message);
		}
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
		void SpawnEnemyWave()
	{
		// TODO: ensure the previous wave is over?
		StartCoroutine(SpawnEnemyWaveCoroutine());
	}

	private IEnumerator SpawnEnemyWaveCoroutine()
	{
		m_waveSpawningInProgress = true;

		RoomController[] sealedRooms = m_waveSealing ? m_avatars.Select(avatar => RoomFromPosition(avatar.transform.position)).ToArray() : null;
		if (m_waveSealing)
		{
			foreach (RoomController room in sealedRooms)
			{
				room.SealRoom(true);
			}
		}

		m_waveWeight += Random.Range(m_waveEscalationMin, m_waveEscalationMax); // TODO: exponential/logistic escalation?

		WeightedObject<GameObject>[] options = m_enemyPrefabs;
		for (float weightRemaining = m_waveWeight; weightRemaining > 0.0f; )
		{
			options = options.Where(weightedObj => weightedObj.m_weight <= weightRemaining).ToArray(); // NOTE that since weightRemaining never increases, it is safe to assume that all previously excluded options are still excluded
			if (options.Length <= 0)
			{
				break; // this shouldn't happen as long as the weights are integers and at least one is 1, but we handle it just in case
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
		if (m_waveSealing && m_enemies.Count == 0 && !m_bossRoomSealed)
		{
			foreach (RoomController room in sealedRooms)
			{
				room.SealRoom(false);
			}
		}
	}

	private void SpawnEnemy(GameObject enemyPrefab)
	{
		Vector3 spawnPos = RoomFromPosition(m_avatars[Random.Range(0, m_avatars.Count())].transform.position).SpawnPointRandom();
		m_enemies.Add(Instantiate(enemyPrefab, spawnPos, Quaternion.identity).GetComponent<EnemyController>());
	}

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		EnemyController enemy = evt.m_object.GetComponent<EnemyController>();
		if (enemy == null)
		{
			return;
		}

		m_enemies.Remove(enemy);

		if (m_waveSealing && m_enemies.Count == 0 && !m_waveSpawningInProgress && !m_bossRoomSealed)
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
			m_timerUI.color = EnemiesRemain() ? Color.red : Color.green;

			if (secondsRemaining >= 1.0f && secondsRemaining <= m_waveWeight + 1.0f)
			{
				source.PlayOneShot(m_timerWarnSFX.RandomWeighted());
			}
		}
	}

	private IEnumerator MusicCoroutine()
	{
		AudioSource source = GetComponent<AudioSource>();

		while (true)
		{
			yield return new WaitForSeconds(Random.Range(m_musicDelayMin, m_musicDelayMax)); // TODO: take into account length of current audio?

			if (!source.isPlaying) // TODO: check boss room ambiance/music
			{
				source.clip = m_ambientMusic.RandomWeighted();
				source.Play();
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
