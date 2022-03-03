using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class GameController : MonoBehaviour
{
	public List<AvatarController> m_avatars;

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

	public float m_waveSecondsMin = 30.0f;
	public float m_waveSecondsMax = 60.0f;
	public float m_waveEscalationMin = 0.0f;
	public float m_waveEscalationMax = 4.0f;


	public static bool IsReloading { get; private set; }

	public static GameController Instance { get; private set; }


	private RoomController m_startRoom;

	private float m_waveWeight = 0.0f;
	private float m_nextWaveTime = 0.0f;

	private readonly List<EnemyController> m_enemies = new();


	private GameController()
	{
		Instance = this;
	}

	private void Start()
	{
		LayoutGenerator generator = new();
		generator.Generate();

		// use generator to spawn rooms/locks/keys/items/etc.
		// TODO: multi-node rooms
		generator.ForEachNode(node =>
		{
			Debug.Assert(node.m_room == null);
			Assert.AreNotEqual(node.m_type, LayoutGenerator.Node.Type.TightCoupling);

			Assert.AreEqual(node.m_type == LayoutGenerator.Node.Type.Entrance, m_startRoom == null);
			if (m_startRoom == null)
			{
				m_startRoom = Instantiate(Utility.RandomWeighted(m_entryRoomPrefabs)).GetComponent<RoomController>();
				m_startRoom.Initialize(node);
			}
			else
			{
				bool spawned = node.TightCoupleParent.m_room.SpawnChildRoom(Utility.RandomWeighted(node.m_type == LayoutGenerator.Node.Type.Boss ? m_bossRoomPrefabs : m_roomPrefabs), node);
				if (!spawned)
				{
					Retry(); // TODO: more efficient way to guarantee room spawning?
					return true;
				}
			}
			return false;
		});

		if (Random.value > 0.5f)
		{
			m_nextWaveTime = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
		}
		StartCoroutine(SpawnWavesCoroutine());

		StartCoroutine(TimerCoroutine());

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

	private void Update()
	{
		Simulation.Tick();
	}


	public void AddAvatar()
	{
		m_avatars.Add(Instantiate(m_avatarPrefab, m_avatars.First().transform.position + Vector3.right, Quaternion.identity).GetComponent<AvatarController>()); // TODO: ensure valid start point

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

			AddCameraTarget(avatar.m_aimObject.transform); // NOTE that we don't replace the whole m_Targets array in case a non-avatar object is also present

			isFirst = false;
		}
	}

	public void AddCameraTarget(Transform tf)
	{
		m_cameraTargetGroup.RemoveMember(tf); // prevent duplication // TODO: necessary?
		m_cameraTargetGroup.AddMember(tf, 1.0f, 1.5f); // TODO: blend weight in? calculate/expose radius?
		// TODO: remove on target destruction?
	}

	public Vector3 RoomPosition(bool checkLocks, GameObject targetObj, float targetMinDistance, bool onFloor)
	{
		return m_startRoom.ChildPosition(checkLocks, targetObj, targetMinDistance, onFloor, true);
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

	public void OnEnemyDespawn(EnemyController enemy)
	{
		m_enemies.Remove(enemy);
	}

	public void OnGameOver()
	{
		ActivateMenu(m_gameOverUI, true);
	}

	public void Save() => PlayerPrefs.Save();

	public void DeleteSave() => PlayerPrefs.DeleteAll(); // TODO: separate setup/configuration from game data?

	public void Retry()
	{
		if (m_pauseUI.isActiveAndEnabled)
		{
			TogglePause();
		}

		if (ConsoleCommands.RegenerateDisabled)
		{
#if DEBUG
			foreach (AvatarController avatar in m_avatars)
			{
				avatar.DebugRespawn();
			}
			Simulation.Schedule<DebugRespawn>();
#endif
			ActivateMenu(m_gameOverUI, false);
			return;
		}

		IsReloading = true;

		// prevent stale GameController asserts while reloading
		foreach (EnemyController enemy in m_enemies)
		{
			enemy.gameObject.SetActive(false);
		}

		SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
		}
	}

	private void SpawnEnemy(GameObject enemyPrefab)
	{
		CapsuleCollider2D enemyCollider = enemyPrefab.GetComponent<CapsuleCollider2D>();
		Vector3 spawnPos = RoomPosition(true, m_avatars[Random.Range(0, m_avatars.Count())].gameObject, 2.0f, !enemyPrefab.GetComponent<KinematicObject>().HasFlying) + Vector3.up * (enemyCollider.size.y * 0.5f - enemyCollider.offset.y); // TODO: base min distance on avatar/enemy size?
		m_enemies.Add(Instantiate(enemyPrefab, spawnPos, Quaternion.identity).GetComponent<EnemyController>());
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
