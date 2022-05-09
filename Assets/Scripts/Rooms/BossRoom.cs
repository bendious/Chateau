using System.Collections.Generic;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent]
public class BossRoom : MonoBehaviour
{
	public WeightedObject<GameObject>[] m_bossPrefabs;

	public WeightedObject<GameObject>[] m_spawnedLadderPrefabs;

	public AudioClip m_audioOutro;


	private Boss m_boss;

	private readonly List<GameObject> m_avatarsPresent = new();


	private void Start()
	{
		// determine farthest valid position from entrance
		Vector3 parentPos = GetComponent<RoomController>().ParentDoorwayPosition;
		GameObject bossPrefab = m_bossPrefabs.RandomWeighted();
		Vector3 spawnPos = transform.position + (Vector3)Utility.OriginToCenterY(bossPrefab);
		Bounds triggerBounds = GetComponent<Collider2D>().bounds;
		spawnPos.x = parentPos.x <= spawnPos.x ? triggerBounds.max.x : triggerBounds.min.x; // TODO: don't assume origin position is closer to min than max?

		// spawn boss
		m_boss = Instantiate(bossPrefab, spawnPos, Quaternion.identity).GetComponent<Boss>();
		m_boss.m_room = this;

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
		AudioSource[] sources = GetComponents<AudioSource>();
		AudioSource source0 = sources.First();
		source0.Play();
		sources[1].PlayScheduled(AudioSettings.dspTime + source0.clip.length);
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
		sources[2].Play();
	}

	public void EndFight()
	{
		AudioSource[] sources = GetComponents<AudioSource>();
		foreach (AudioSource source in sources)
		{
			source.Stop(); // TODO: soft outro?
		}

		// NOTE that we have to use a component that won't be playing any more audio rather than using PlayOneShot(), which leaves the music running even after level load
		sources.First().clip = GameController.Instance.m_victoryAudio;
		sources.First().Play();

		// reset room entrance(s)
		GetComponent<RoomController>().SealRoom(false);
	}


#if DEBUG
	private void DebugOnRespawn(DebugRespawn evt)
	{
		EndFight();

		GameController.Instance.m_bossRoomSealed = false;

		// reset boss
		m_boss.GetComponent<Boss>().DebugReset();

		// re-enable trigger
		GetComponent<Collider2D>().enabled = true;
	}
#endif
}
