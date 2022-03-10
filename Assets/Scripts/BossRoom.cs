using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class BossRoom : MonoBehaviour
{
	public Boss m_boss;

	public AudioClip m_audioOutro;


	private struct DoorInfo
	{
		public GameObject m_object;
		public Bounds m_bounds;
	};
	private DoorInfo[] m_doors;

	private readonly List<GameObject> m_avatarsPresent = new();

	private readonly List<GameObject> m_spawnedGates = new();


	private void Awake()
	{
		m_boss.transform.SetParent(null); // necessary since all prefab contents have to start as children of the main object

		// add event handlers
#if DEBUG
		DebugRespawn.OnExecute += DebugOnRespawn;
#endif

		// track doorways
		RoomController roomScript = GetComponent<RoomController>();
		m_doors = new DoorInfo[roomScript.m_doorways.Length];
		int i = 0;
		foreach (GameObject doorway in roomScript.m_doorways)
		{
			m_doors[i++] = new DoorInfo { m_object = doorway, m_bounds = doorway.GetComponent<Collider2D>().bounds };
		}
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!GameController.Instance.m_avatars.Exists(avatar => collider.gameObject == avatar.gameObject))
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
