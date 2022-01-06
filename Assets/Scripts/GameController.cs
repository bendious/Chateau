using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Platformer.Core;
using Platformer.Mechanics;
using UnityEngine;


public class GameController : MonoBehaviour
{
	public AvatarController m_avatar;

	public GameObject m_roomPrefab;
	public GameObject[] m_enemyPrefabs;
	public float[] m_enemyPrefabWeights;
	public GameObject m_victoryZonePrefab;

	public TMPro.TMP_Text m_timerUI;
	public Canvas m_pauseUI;
	public Canvas m_gameOverUI;

	public AudioClip m_victoryAudio;

	public float m_waveSecondsMin = 30.0f;
	public float m_waveSecondsMax = 60.0f;
	public int m_waveEnemiesMin = 0;
	public int m_waveEnemiesMax = 10;


	private RoomController m_startRoom;
	private GameObject m_victoryZone;

	private float m_nextWaveTime = 0.0f;

	private readonly List<EnemyController> m_enemies = new List<EnemyController>();


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
	}

	private void Update()
	{
		Simulation.Tick();

		if (!m_gameOverUI.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape))
		{
			TogglePause();
		}
	}


	public Vector3 FloorPosition(bool checkLocks, GameObject targetObj)
	{
		return m_startRoom.ChildFloorPosition(checkLocks, targetObj);
	}

	public void TogglePause()
	{
		Time.timeScale = Time.timeScale == 0.0f ? 1.0f : 0.0f;
		m_pauseUI.gameObject.SetActive(!m_pauseUI.gameObject.activeSelf);
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
		RoomController endRoom = m_startRoom.LeafRoomFarthest().Item1;
		m_victoryZone = Instantiate(m_victoryZonePrefab, endRoom.transform.position, Quaternion.identity);
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
		int enemyCount = Random.Range(m_waveEnemiesMin, m_waveEnemiesMax + 1);
		Transform avatarTf = m_avatar.transform;

		for (int i = 0; i < enemyCount; ++i)
		{
			Vector3 spawnPos = FloorPosition(true, m_avatar.gameObject);
			EnemyController enemy = Instantiate(EnemyPrefabRandom(), spawnPos, Quaternion.identity).GetComponent<EnemyController>();
			enemy.m_target = avatarTf;
			m_enemies.Add(enemy);
		}
	}

	private GameObject EnemyPrefabRandom()
	{
		return Utility.RandomWeighted(m_enemyPrefabs, m_enemyPrefabWeights);
	}

	private IEnumerator TimerCoroutine()
	{
		while (m_nextWaveTime >= 0.0f)
		{
			yield return new WaitForSeconds(1.0f); // NOTE that we currently don't care whether the UI timer is precise within partial seconds
			m_timerUI.text = System.TimeSpan.FromSeconds(m_nextWaveTime - Time.time).ToString("m':'ss");
			Color color = EnemiesRemain() ? Color.red : Color.green;
			m_timerUI.color = color;
			if (m_victoryZone != null)
			{
				m_victoryZone.GetComponent<SpriteRenderer>().color = color;
			}
		}
	}
}
