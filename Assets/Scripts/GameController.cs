using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;


public class GameController : MonoBehaviour
{
	public AvatarController m_avatar;

	public DialogueController m_dialogueController;

	public Canvas m_overlayCanvas;

	public GameObject m_roomPrefab;
	public WeightedObject<GameObject>[] m_enemyPrefabs;
	public GameObject m_victoryZonePrefab;

	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public AudioClip m_victoryAudio;

	public float m_waveSecondsMin = 30.0f;
	public float m_waveSecondsMax = 60.0f;
	public float m_waveEscalationMin = 0.0f;
	public float m_waveEscalationMax = 2.0f;


	private RoomController m_startRoom;

	private float m_waveWeight = 0.0f;
	private float m_nextWaveTime = 0.0f;

	private readonly List<EnemyController> m_enemies = new();


	private void Start()
	{
		m_startRoom = Instantiate(m_roomPrefab).GetComponent<RoomController>();
		m_startRoom.m_roomPrefab = m_roomPrefab; // NOTE that since Unity's method of internal prefab references doesn't allow a script to reference the prefab that contains it, we have to manually update the child's reference here
		StartCoroutine(SpawnVictoryZoneWhenReady());

		if (Random.value > 0.5f)
		{
			m_nextWaveTime = Random.Range(m_waveSecondsMin, m_waveSecondsMax);
		}
		StartCoroutine(SpawnWavesCoroutine());

		StartCoroutine(TimerCoroutine());

		// TODO: dialogue system
		m_dialogueController.Play(new string[] { "Ah, welcome home.", "You’ve been out for quite a while, haven’t you?", "You’re not going to claim grounds for outrage if a few... uninvited guests have shown up in the mean time, are you?", "But don’t worry about me; you have more pressing concerns at the moment, I believe." });
	}

	private void Update()
	{
		Simulation.Tick();

		if (!m_gameOverUI.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape))
		{
			TogglePause();
		}
	}


	public Vector3 RoomPosition(bool checkLocks, GameObject targetObj, bool onFloor)
	{
		return m_startRoom.ChildPosition(checkLocks, targetObj, onFloor);
	}

	public List<Vector2> Pathfind(Vector2 startPos, Vector2 targetPos, Vector2 offsetMag)
	{
		return m_startRoom.ChildRoomPath(startPos, targetPos, offsetMag);
	}

	public void TogglePause()
	{
		Time.timeScale = Time.timeScale == 0.0f ? 1.0f : 0.0f;
		m_pauseUI.gameObject.SetActive(!m_pauseUI.gameObject.activeSelf);
		// NOTE that if the avatar is ever visible while paused, we should disable its script here to avoid continuing to update facing
	}

	public bool ToggleOverlay(SpriteRenderer sourceRenderer, string text)
	{
		GameObject overlayObj = m_overlayCanvas.gameObject;
		if (!overlayObj.activeSelf)
		{
			Image overlayImage = overlayObj.GetComponentInChildren<Image>();
			overlayImage.sprite = sourceRenderer.sprite;
			overlayImage.color = sourceRenderer.color;
			overlayObj.GetComponentInChildren<TMPro.TMP_Text>().text = text;
		}
		overlayObj.SetActive(!overlayObj.activeSelf);
		return overlayObj.activeSelf;
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
		m_gameOverUI.gameObject.SetActive(true);
	}

	public void Retry()
	{
		// TODO: re-generate?

		m_avatar.OnSpawn();
		m_gameOverUI.gameObject.SetActive(false);
	}

	public void OnVictory()
	{
		m_avatar.OnVictory();
		m_timerUI.text = "WIN!";
		m_nextWaveTime = -1.0f;
		StopAllCoroutines();
		m_avatar.GetComponent<AudioSource>().PlayOneShot(m_victoryAudio);
		// TODO: roll credits / etc.
	}

	public void Quit()
	{
		// TODO: save?

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


	private IEnumerator SpawnVictoryZoneWhenReady()
	{
		yield return new WaitUntil(() => m_startRoom.AllChildrenReady());

		// color path to victory
		Color colorDefault = Color.gray; // TODO: don't hardcode default color
		List<RoomController> endRoomPath = m_startRoom.RoomPathLongest().Item1;
		int pathIdx = 0;
		foreach (RoomController room in endRoomPath)
		{
			++pathIdx;
			float pathPct = (float)pathIdx / endRoomPath.Count;
			Color color = colorDefault;
			color = Color.Lerp(color, Color.white, pathPct);
			foreach (SpriteRenderer renderer in room.transform.GetComponentsInChildren<SpriteRenderer>())
			{
				if (renderer.color == colorDefault)
				{
					renderer.color = color;
				}
			}
		}

		// create victory zone
		Instantiate(m_victoryZonePrefab, endRoomPath.Last().transform.position, Quaternion.identity);
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
		Vector3 spawnPos = RoomPosition(true, m_avatar.gameObject, !enemyPrefab.GetComponent<KinematicObject>().HasFlying) + Vector3.up * (enemyCollider.size.y * 0.5f - enemyCollider.offset.y);
		EnemyController enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity).GetComponent<EnemyController>();
		enemy.m_target = m_avatar.transform;
		m_enemies.Add(enemy);
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
}
