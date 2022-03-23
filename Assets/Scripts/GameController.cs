using Cinemachine;
using System.Collections;
using System.Collections.Generic;
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
	public WeightedObject<GameObject>[] m_enemyPrefabs;

	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public MaterialSystem m_materialSystem;

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
		bool failed = generator.ForEachNodeDepthFirst(node =>
		{
			Debug.Assert(node.m_room == null);
			Assert.AreNotEqual(node.m_type, LayoutGenerator.Node.Type.TightCoupling);

			LayoutGenerator.Node parent = node.TightCoupleParent;
			if (parentPending != null && parent != parentPending && nodesPending.Count > 0 && nodesPending.First().DirectParentsInternal != parent?.DirectParentsInternal)
			{
				bool spawned = AddRoomsForNodes(nodesPending.ToArray());
				if (!spawned)
				{
					return true;
				}
				nodesPending.Clear();
			}

			parentPending = parent;
			nodesPending.Add(node);
			return false;
		});
		failed = failed || !AddRoomsForNodes(nodesPending.ToArray());
		if (failed)
		{
			Retry(); // TODO: more efficient way to guarantee room spawning?
			return;
		}

		m_startRoom.SpawnKeysRecursive();

		// TODO: dialogue system
		if (!PlayerPrefs.HasKey("IntroDialogueDone"))
		{
			m_dialogueController.Play(new string[] { "Ah, welcome home.", "You’ve been out for quite a while, haven’t you?", "You’re not going to claim grounds for outrage if a few... uninvited guests have shown up in the mean time, are you?", "But don’t worry about me; you have more pressing concerns at the moment, I believe." }, () =>
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

	public Vector3 RoomSpawnPosition(Vector2 position)
	{
		return m_startRoom.RoomFromPosition(position).SpawnPointRandom();
	}

	public List<Vector2> Pathfind(Vector2 startPos, Vector2 targetPos, Vector2 offsetMag)
	{
		return m_startRoom.ChildPositionPath(startPos, targetPos, offsetMag, true);
	}

	public void TogglePause()
	{
		if (m_gameOverUI != null && m_gameOverUI.gameObject.activeSelf)
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

	public void Save() => PlayerPrefs.Save();

	public void DeleteSave() => PlayerPrefs.DeleteAll(); // TODO: separate setup/configuration from game data?

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
				avatar.Respawn();
			}
			Simulation.Schedule<DebugRespawn>();
			ActivateMenu(m_gameOverUI, false);
			return;
		}

		LoadScene(SceneManager.GetActiveScene().name);
	}

	public void LoadScene(string name)
	{
		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause();
		}

		IsReloading = true;

		// prevent stale GameController asserts while reloading
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.gameObject.SetActive(false);
		}

		SceneManager.LoadScene(name);
	}

	public void OnVictory()
	{
		foreach (AvatarController avatar in m_avatars)
		{
			avatar.OnVictory();
		}
		m_timerUI.text = "WIN!";
		m_nextWaveTime = -1.0f;
		StopAllCoroutines();
		m_avatars.First().GetComponent<AudioSource>().PlayOneShot(m_victoryAudio);
		// TODO: roll credits / etc.
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


	private bool AddRoomsForNodes(LayoutGenerator.Node[] nodes)
	{
		List<LayoutGenerator.Node> nodesShuffled = nodes.OrderBy(node => Random.value).ToList();
		List<List<LayoutGenerator.Node>> nodesSplit = new();
		for (int i = 0; i < nodesShuffled.Count; )
		{
			nodesSplit.Add(new());
			int numDoors = 0;
			int doorsMax = m_startRoom == null ? 4 : 3; // TODO: determine based on room prefab?
			bool isDoor = false;
			do
			{
				LayoutGenerator.Node node = nodesShuffled[i];
				nodesSplit.Last().Add(node);
				isDoor = node.m_type == LayoutGenerator.Node.Type.Lock || node.m_type == LayoutGenerator.Node.Type.Secret; // TODO: function?
				if (isDoor)
				{
					++numDoors;
				}
				++i;
			} while ((!isDoor || numDoors < doorsMax) && i < nodesShuffled.Count);
		}

		foreach (List<LayoutGenerator.Node> nodesList in nodesSplit)
		{
			if (m_startRoom == null)
			{
				m_startRoom = Instantiate(Utility.RandomWeighted(m_entryRoomPrefabs)).GetComponent<RoomController>();
				m_startRoom.Initialize(nodesList.ToArray());
			}
			else
			{
				// find room to spawn from
				LayoutGenerator.Node ancestorSpawned = nodesList.First();
				do
				{
					ancestorSpawned = ancestorSpawned.TightCoupleParent;
				}
				while (ancestorSpawned.m_room == null);

				// try spawning prefabs in random order
				bool success = false;
				WeightedObject<GameObject>[] prefabsOrdered = nodesList.Exists(node => node.m_type == LayoutGenerator.Node.Type.Boss) ? m_bossRoomPrefabs : m_roomPrefabs;
				foreach (GameObject roomPrefab in Utility.RandomWeightedOrder(prefabsOrdered))
				{
					success = ancestorSpawned.m_room.SpawnChildRoom(roomPrefab, nodesList.ToArray());
					if (success)
					{
						break;
					}
				}
				if (!success)
				{
					return false;
				}
			}
		}
		return true;
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
			avatar.Controls.SwitchCurrentActionMap(m_pauseUI.gameObject.activeSelf || (m_gameOverUI != null && m_gameOverUI.gameObject.activeSelf) || avatar.m_overlayCanvas.gameObject.activeSelf ? "UI" : "Avatar"); // TODO: account for other UI instances?
		}
	}
}
