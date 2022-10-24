using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class BossRoom : MonoBehaviour
{
	[SerializeField] private WeightedObject<Boss>[] m_bossPrefabs;
	[SerializeField] private float m_bossHeightMin = 0.0f;
	[SerializeField] private float m_bossHeightMax = 0.0f;

	[SerializeField] private WeightedObject<GameObject>[] m_platformPrefabs;
	[SerializeField] private int m_platformsMin = 0;
	[SerializeField] private int m_platformsMax = 4;

	public WeightedObject<GameObject>[] m_spawnedLadderPrefabs;

	[SerializeField] private AudioClip m_audioOutro;
	[SerializeField] private AudioClip m_music;


	private Boss m_boss;

	private readonly List<GameObject> m_avatarsPresent = new();


	private void Start()
	{
		// determine farthest valid position from entrance
		RoomController room = GetComponent<RoomController>();
		Vector3 parentPos = room.ParentDoorwayPosition;
		Boss bossPrefab = m_bossPrefabs.RandomWeighted();
		Vector3 spawnPos = transform.position + (Vector3)bossPrefab.gameObject.OriginToCenterY();
		Bounds triggerBounds = GetComponent<Collider2D>().bounds;
		spawnPos.x = parentPos.x <= spawnPos.x ? triggerBounds.max.x : triggerBounds.min.x; // TODO: don't assume origin position is closer to min than max?
		spawnPos.y += Random.Range(m_bossHeightMin, m_bossHeightMax);

		// spawn boss
		m_boss = Instantiate(bossPrefab, spawnPos, Quaternion.identity);
		m_boss.m_room = this;

		// spawn platforms
		for (int i = 0, n = Random.Range(m_platformsMin, m_platformsMax + 1); i < n; ++i)
		{
			GameObject platform = m_platformPrefabs.RandomWeighted();
			Instantiate(platform, room.InteriorPosition(float.MaxValue, platform, edgeBuffer: 1.0f), Quaternion.identity, room.transform); // TODO: more deliberate layout / ensure jumpability?
		}

		// add event handlers
#if DEBUG
		DebugRespawn.OnExecute += DebugOnRespawn;
#endif
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (GameController.Instance.m_avatars.Count <= 0 || !GameController.Instance.m_avatars.Exists(avatar => collider.gameObject == avatar.gameObject))
		{
			return;
		}
		m_avatarsPresent.Add(collider.gameObject);

		// ensure all co-op players are present
		// TODO: allow sealing while awaiting respawn, once sealed room spawn positions are handled?
		if (GameController.Instance.m_avatars.Exists(avatar => !avatar.IsAlive || !m_avatarsPresent.Contains(avatar.gameObject)))
		{
			return;
		}

		GetComponent<Collider2D>().enabled = false;

		// seal room
		GetComponent<RoomController>().SealRoom(true);
		GameController.Instance.m_bossRoomSealed = true;

		// play ambiance SFX
		// TODO: use MusicManager to prevent music starting during ambiance (once it supports multiple sources)
		AudioSource[] sources = GetComponents<AudioSource>();
		AudioSource source0 = sources.First();
		source0.Play();
		AudioSource source1 = sources[1];
		source1.PlayScheduled(AudioSettings.dspTime + source0.clip.length);
		GameController.Instance.GetComponent<MusicManager>().FadeOut(source0.clip.length + source1.clip.length); // TODO: more deliberate fade-out time?
	}

	private void OnTriggerExit2D(Collider2D collider)
	{
		m_avatarsPresent.Remove(collider.gameObject);
	}

	private void OnDestroy()
	{
#if DEBUG
		DebugRespawn.OnExecute -= DebugOnRespawn;
#endif
	}


	public void AmbianceToMusic()
	{
		AudioSource[] sources = GetComponents<AudioSource>();
		sources[1].loop = false;
		sources.First().clip = m_audioOutro;
		sources.First().PlayScheduled(AudioSettings.dspTime + sources[1].clip.length - sources[1].time);
		GameController.Instance.GetComponent<MusicManager>().Play(m_music);
	}

	public void EndFight()
	{
		AudioSource[] sources = GetComponents<AudioSource>();
		foreach (AudioSource source in sources)
		{
			source.Stop(); // TODO: soft outro?
		}

		// reset room entrance(s)
		GetComponent<RoomController>().SealRoom(false);
	}


#if DEBUG
	private void DebugOnRespawn(DebugRespawn evt)
	{
		EndFight();

		GameController.Instance.m_bossRoomSealed = false;

		// reset boss
		if (m_boss != null)
		{
			m_boss.DebugReset();
		}

		// re-enable trigger
		GetComponent<Collider2D>().enabled = true;
	}
#endif
}
