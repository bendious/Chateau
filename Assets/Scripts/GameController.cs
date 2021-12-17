using Platformer.Core;
using Platformer.Mechanics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class GameController : MonoBehaviour
{
	public AvatarController m_avatar;

	public GameObject m_roomPrefab;
	public GameObject m_enemyPrefab;
	public GameObject m_victoryZonePrefab;

	public Text m_timerUI;

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
	}


	public bool EnemiesRemain()
	{
		return m_enemies.Count > 0;
	}

	public void OnEnemyDespawn(EnemyController enemy)
	{
		m_enemies.Remove(enemy);
	}

	public void OnVictory()
	{
		m_avatar.OnVictory();
		m_timerUI.text = "WIN!";
		m_nextWaveTime = -1.0f;
		StopAllCoroutines();
		// TODO: roll credits / etc.
	}


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

	private void SpawnEnemyWave()
	{
		// TODO: more deliberate spawning, room knowledge
		const float offsetMagMin = 1.0f;
		const float offsetMagMax = 5.0f;

		int enemyCount = Random.Range(m_waveEnemiesMin, m_waveEnemiesMax + 1);
		Transform avatarTf = m_avatar.transform;

		for (int i = 0; i < enemyCount; ++i)
		{
			Vector3 spawnCenterPos = avatarTf.position + new Vector3(Random.Range(offsetMagMin, offsetMagMax) * (Random.value > 0.5f ? -1.0f : 1.0f), 0.0f, 0.0f);
			EnemyController enemy = Instantiate(m_enemyPrefab, spawnCenterPos, Quaternion.identity).GetComponent<EnemyController>();
			enemy.m_target = avatarTf;
			m_enemies.Add(enemy);
		}
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
