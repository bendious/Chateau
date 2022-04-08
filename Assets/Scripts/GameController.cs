using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class GameController : MonoBehaviour
{
	public LayoutGenerator.Node.Type m_type;

	public List<AvatarController> m_avatars = new();

	public CinemachineVirtualCamera m_virtualCamera;
	public CinemachineTargetGroup m_cameraTargetGroup;

	public DialogueController m_dialogueController;

	public GameObject m_avatarPrefab;
	public WeightedObject<GameObject>[] m_entryRoomPrefabs;
	public WeightedObject<GameObject>[] m_roomPrefabs;
	public WeightedObject<GameObject>[] m_bossRoomPrefabs;
	public WeightedObject<RoomType>[] m_roomTypes;
	public WeightedObject<GameObject>[] m_enemyPrefabs;

	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public MaterialSystem m_materialSystem;
	public SavableFactory m_savableFactory;

	public AudioClip m_victoryAudio;

	public float m_waveSecondsMin = 45.0f;
	public float m_waveSecondsMax = 90.0f;
	public float m_waveStartWeight = 1.0f;
	public float m_waveEscalationMin = 0.0f;
	public float m_waveEscalationMax = 4.0f;
	public float m_waveEnemyDelayMin = 0.5f;
	public float m_waveEnemyDelayMax = 2.0f;

	[HideInInspector]
	public bool m_bossRoomSealed = false;


	public static bool IsReloading { get; private set; }

	public static GameController Instance { get; private set; }


	public RoomController LootRoom { get; private set; }

	public bool Victory { get; private set; }


	[SerializeField]
	private string[] m_savableTags;


	private RoomController m_startRoom;

	private float m_waveWeight;
	private float m_nextWaveTime = 0.0f;
	private bool m_waveSpawningInProgress = false;

	private readonly List<EnemyController> m_enemies = new();


	private void Awake()
	{
		Instance = this;
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
		bool failed = generator.ForEachNodeDepthFirst(node =>
		{
			Debug.Assert(node.m_room == null);
			Assert.AreNotEqual(node.m_type, LayoutGenerator.Node.Type.TightCoupling);

			LayoutGenerator.Node parent = node.TightCoupleParent;
			if (parent != parentPending && nodesPending.Count > 0 && nodesPending.First().DirectParentsInternal != parent?.DirectParentsInternal)
			{
				roomCount = AddRoomsForNodes(nodesPending.ToArray(), roomCount);
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
		failed = failed || AddRoomsForNodes(nodesPending.ToArray(), roomCount) == 0;
		if (failed)
		{
			Victory = true; // to prevent clearing avatars' inventory // TODO: better flag?
			Retry(); // TODO: more efficient way to guarantee room spawning?
			return;
		}

		Load();

		m_startRoom.FinalizeRecursive();

		// TODO: dialogue system
		if (!PlayerPrefs.HasKey("IntroDialogueDone"))
		{
			m_dialogueController.Play(null, Color.black, new string[] { "Ah, welcome home.", "You�ve been out for quite a while, haven�t you?", "You�re not going to claim grounds for outrage if a few... uninvited guests have shown up in the mean time, are you?", "But don�t worry about me; you have more pressing concerns at the moment, I believe." }, () =>
			{
				PlayerPrefs.SetInt("IntroDialogueDone", 1);
				Save(); // TODO: auto-save system?
			});
		}

		IsReloading = false;
	}

	private void OnDestroy()
	{
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
		if (m_avatars.Count == 0 && m_enemyPrefabs.Length > 0)
		{
			if (Random.value > 0.5f)
			{
				m_nextWaveTime = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
			}
			StopAllCoroutines();
			StartCoroutine(SpawnWavesCoroutine());
			StartCoroutine(TimerCoroutine());
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
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Called via SendMessage() from PlayerInputManager component")]
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
		return m_startRoom.RoomFromPosition(position);
	}

	public List<Vector2> Pathfind(Vector2 startPos, Vector2 targetPos, Vector2 offsetMag)
	{
		return m_startRoom.ChildPositionPath(startPos, targetPos, offsetMag, true);
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

	public bool EnemiesRemain()
	{
		return m_enemies.Count > 0;
	}

	public void RemoveUnreachableEnemies()
	{
		// TODO: don't assume we're locked into individual rooms?
		RoomController[] reachableRooms = m_avatars.Where(avatar => avatar.IsAlive).Select(avatar => m_startRoom.RoomFromPosition(avatar.transform.position)).ToArray();

		foreach (EnemyController enemy in m_enemies)
		{
			if (reachableRooms.Contains(m_startRoom.RoomFromPosition(enemy.transform.position)))
			{
				continue;
			}
			Simulation.Schedule<ObjectDespawn>().m_object = enemy.gameObject;
		}
	}

	public void OnGameOver()
	{
		ActivateMenu(m_gameOverUI, true);
	}

	public void Save()
	{
		PlayerPrefs.Save();

		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0) // TODO: don't assume only first scene stores contents?
		{
			return;
		}

		using SaveWriter saveFile = new(activeScene);

		Object[] savables = m_savableTags.SelectMany(tag => GameObject.FindGameObjectsWithTag(tag)).ToArray();
		saveFile.Write(savables.Length);
		foreach (GameObject obj in savables)
		{
			ISavable.Save(saveFile, obj.GetComponent<ISavable>());
		}
	}

	public void Load()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (activeScene.buildIndex != 0) // TODO: don't assume only first scene stores contents?
		{
			return;
		}

		try
		{
			using SaveReader saveFile = new(activeScene);
			if (!saveFile.IsOpen)
			{
				return;
			}

			saveFile.Read(out int savablesCount);
			for (int i = 0; i < savablesCount; ++i)
			{
				ISavable.Load(saveFile);
			}
		}
		catch (System.Exception e)
		{
			Debug.LogError("Invalid save file: " + e.Message);
		}
	}

	public void DeleteSave()
	{
		PlayerPrefs.DeleteAll(); // TODO: separate setup/configuration from game data
		SaveHelpers.Delete(SceneManager.GetSceneAt(0)); // TODO: don't assume only first scene stores contents?
	}

	public void Retry()
	{
		if (ConsoleCommands.RegenerateDisabled)
		{
			if (m_pauseUI.isActiveAndEnabled)
			{
				TogglePause();
			}
			foreach (AvatarController avatar in m_avatars)
			{
				avatar.Respawn(!Victory || SceneManager.GetActiveScene().buildIndex == 0); // TODO: leave held items in inventory but also save them w/o duplicating them
			}
			Simulation.Schedule<DebugRespawn>();
			ActivateMenu(m_gameOverUI, false);
			return;
		}

		LoadScene(SceneManager.GetActiveScene().name);
	}

	public void LoadScene(string name)
	{
		Save();

		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause();
		}
		ActivateMenu(m_gameOverUI, false);

		IsReloading = true;

		// prevent stale GameController asserts while reloading
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.gameObject.SetActive(false);
		}

		foreach (AvatarController avatar in m_avatars)
		{
			avatar.Respawn(!Victory || SceneManager.GetActiveScene().buildIndex == 0); // TODO: leave held items in inventory but also save them w/o duplicating them
		}

		SceneManager.LoadScene(name);
	}

	public void OnVictory()
	{
		Victory = true;
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.OnVictory();
		}
		m_timerUI.text = "WIN!";
		m_nextWaveTime = -1.0f;
		StopAllCoroutines();

		// TODO: roll credits / etc.?
	}

	public void Quit()
	{
		Save(); // TODO: prompt player?

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
	}

	public void DebugKillAllEnemies()
	{
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.GetComponent<Health>().Die();
		}
	}
#endif


	private int AddRoomsForNodes(LayoutGenerator.Node[] nodes, int roomCount)
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
				if ((node.m_type == LayoutGenerator.Node.Type.RoomVertical || node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) && (newList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical || node.m_type == LayoutGenerator.Node.Type.RoomHorizontal)))
				{
					break; // start new room
				}
				newList.Add(node);
				isDoor = node.m_type == LayoutGenerator.Node.Type.Lock || node.m_type == LayoutGenerator.Node.Type.Secret; // TODO: function?
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
					nodesSplit.Enqueue(nodesList);
					continue;
				}

				// try spawning prefabs in random order
				RoomController childRoom = null;
				WeightedObject<GameObject>[] prefabsOrdered = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.Boss) ? m_bossRoomPrefabs : m_roomPrefabs;
				Vector2[] allowedDirections = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomVertical) ? new Vector2[] { Vector2.down, Vector2.up } : nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.RoomHorizontal) ? new Vector2[] { Vector2.left, Vector2.right } : null;
				foreach (GameObject roomPrefab in prefabsOrdered.RandomWeightedOrder())
				{
					childRoom = spawnRoom.SpawnChildRoom(roomPrefab, nodesList.ToArray(), allowedDirections);
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

	private IEnumerator SpawnWavesCoroutine()
	{
		while (m_nextWaveTime >= 0.0f)
		{
			yield return new WaitForSeconds(m_nextWaveTime - Time.time);
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

		RoomController[] rooms = m_avatars.Select(avatar => m_startRoom.RoomFromPosition(avatar.transform.position)).ToArray();
		foreach (RoomController room in rooms) // TODO: seal all rooms?
		{
			room.SealRoom(true);
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
			WeightedObject<GameObject> weightedEnemyPrefab = options[Random.Range(0, options.Length)];
			SpawnEnemy(weightedEnemyPrefab.m_object);

			weightRemaining -= weightedEnemyPrefab.m_weight;

			yield return new WaitForSeconds(Random.Range(m_waveEnemyDelayMin, m_waveEnemyDelayMax));
		}

		m_waveSpawningInProgress = false;

		// unseal rooms if the last enemy was killed immediately
		if (m_enemies.Count == 0 && !m_bossRoomSealed)
		{
			foreach (RoomController room in rooms)
			{
				room.SealRoom(false);
			}
		}
	}

	private void SpawnEnemy(GameObject enemyPrefab)
	{
		Vector3 spawnPos = m_startRoom.RoomFromPosition(m_avatars[Random.Range(0, m_avatars.Count())].transform.position).SpawnPointRandom();
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

		if (m_enemies.Count == 0 && !m_waveSpawningInProgress && !m_bossRoomSealed)
		{
			// TODO: slight time delay?
			foreach (RoomController room in m_avatars.Select(avatar => m_startRoom.RoomFromPosition(avatar.transform.position)))
			{
				room.SealRoom(false);
			}
		}
	}

	private IEnumerator TimerCoroutine()
	{
		while (m_nextWaveTime >= 0.0f)
		{
			yield return new WaitForSeconds(1.0f); // NOTE that we currently don't care whether the UI timer is precise within partial seconds
			m_timerUI.text = System.TimeSpan.FromSeconds(m_nextWaveTime - Time.time).ToString("m':'ss");
			m_timerUI.color = EnemiesRemain() ? Color.red : Color.green;
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
