using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class BossRoom : MonoBehaviour
{
	public WeightedObject<GameObject>[] m_bossPrefabs;

	public AudioClip m_audioOutro;


	private Boss m_boss;

	private readonly List<GameObject> m_avatarsPresent = new();

	private readonly List<GameObject> m_spawnedGates = new();


	private void Start()
	{
		// determine entrance
		// TODO: don't assume exactly one open doorway?
		Vector3 entrancePos = Vector3.zero;
		foreach (GameObject doorway in GetComponent<RoomController>().m_doorways)
		{
			if (!doorway.activeSelf)
			{
				entrancePos = doorway.transform.position;
				break;
			}
		}

		// determine farthest valid position from entrance
		GameObject bossPrefab = Utility.RandomWeighted(m_bossPrefabs);
		Vector3 spawnPos = transform.position + new Vector3(0.0f, -bossPrefab.GetComponents<CapsuleCollider2D>().Min(collider => collider.offset.y - collider.size.y * 0.5f), 0.0f); // NOTE that we can't use Collider2D.bounds on an uninstantiated prefab // TODO: don't assume capsule-shaped collider?
		Bounds triggerBounds = GetComponent<Collider2D>().bounds;
		spawnPos.x = entrancePos.x <= spawnPos.x ? triggerBounds.max.x : triggerBounds.min.x; // TODO: don't assume bottom/top doors are closer to the left?

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
		// NOTE that the first source should have been played by RoomController.SealRoom()
		AudioSource[] sources = GetComponents<AudioSource>();
		sources[1].PlayScheduled(AudioSettings.dspTime + sources.First().clip.length);
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

	public void EndMusic()
	{
		foreach (AudioSource source in GetComponents<AudioSource>())
		{
			source.Stop(); // TODO: soft outro?
		}
	}


#if DEBUG
	private void DebugOnRespawn(DebugRespawn evt)
	{
		EndMusic();

		// reset room entrance(s)
		foreach (GameObject gate in m_spawnedGates)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = gate;
		}
		m_spawnedGates.Clear();
		GameController.Instance.m_bossRoomSealed = false;

		// reset boss
		m_boss.GetComponent<Boss>().DebugReset();

		// re-enable trigger
		GetComponent<Collider2D>().enabled = true;
	}
#endif
}
