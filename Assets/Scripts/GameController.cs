using Platformer.Core;
using Platformer.Mechanics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class GameController : MonoBehaviour
{
	public GameObject m_avatar;

	public GameObject m_roomPrefab;
	public GameObject m_enemyPrefab;

	public Text m_timerUI;

	public float m_waveSecondsMin = 30.0f;
	public float m_waveSecondsMax = 60.0f;
	public int m_waveEnemiesMin = 0;
	public int m_waveEnemiesMax = 10;


	private float m_nextWaveTime = -1.0f;

	private List<EnemyController> m_enemies = new List<EnemyController>();


	private void Start()
	{
		Instantiate(m_roomPrefab).GetComponent<RoomController>().m_roomPrefab = m_roomPrefab; // NOTE that since Unity's method of internal prefab references doesn't allow a script to reference the prefab that contains it, we have to manually update the child's reference here
	}

	private void Update()
	{
		Simulation.Tick();

		if (Time.time > m_nextWaveTime)
		{
			SpawnEnemyWave();
			m_nextWaveTime = Time.time + Random.Range(m_waveSecondsMin, m_waveSecondsMax);
		}

		m_timerUI.text = System.TimeSpan.FromSeconds(m_nextWaveTime - Time.time).ToString("m':'ss");
		m_timerUI.color = m_enemies.Count == 0 ? Color.green : Color.red;
	}


	public void OnEnemyDespawn(EnemyController enemy)
	{
		m_enemies.Remove(enemy);
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
}
