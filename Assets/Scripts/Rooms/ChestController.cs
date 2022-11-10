using System.Collections.Generic;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(AudioSource))]
public class ChestController : FurnitureController, IInteractable, IUnlockable
{
	[SerializeField] private WeightedObject<GameObject>[] m_keyPrefabs;
	[SerializeField] private WeightedObject<AudioClip>[] m_unlockSFX;
	[SerializeField] private Sprite m_spriteUnlocked;
	[SerializeField] private Sprite m_spriteOpen; // TODO: use animation?
	[SerializeField] private WeightedObject<AudioClip>[] m_openSFX;
	[SerializeField] private Vector2 m_openSizePcts = new(1.0f, 0.5f);


	public GameObject Parent { get => null; set => Debug.LogError("Cannot set ChestController.Parent"); }
	public bool IsLocked => m_isLocked;

	public bool IsCriticalPath { private get; set; }


	private GameObject m_keyObj;
	private bool m_isLocked = true;
	private bool m_isOpen = false;
	private readonly List<GameObject> m_prespawned = new();


	public override List<GameObject> SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining, List<GameObject> prefabsSpawned, float sizeScalarX = 1.0f, float sizeScalarY = 1.0f)
	{
		List<GameObject> items = base.SpawnItems(rare, roomType, itemCountExisting, furnitureRemaining, prefabsSpawned, Mathf.Min(1.0f, sizeScalarX * m_openSizePcts.x), Mathf.Min(1.0f, sizeScalarY * m_openSizePcts.y));
		if (!m_isOpen)
		{
			foreach (GameObject obj in items)
			{
				obj.SetActive(false);
				m_prespawned.Add(obj);
			}
		}
		return items;
	}

	public override GameObject SpawnKey(GameObject prefab, bool isCriticalPath, float sizeScalarX = 1.0f, float sizeScalarY = 1.0f)
	{
		GameObject obj = base.SpawnKey(prefab, isCriticalPath, Mathf.Min(1.0f, sizeScalarX * m_openSizePcts.x), Mathf.Min(1.0f, sizeScalarY * m_openSizePcts.y));
		if (!m_isOpen)
		{
			obj.SetActive(false);
			m_prespawned.Add(obj);
			if (isCriticalPath)
			{
				IsCriticalPath = true;
				if (m_keyObj != null)
				{
					m_keyObj.GetComponent<ItemController>().IsCriticalPath = true;
				}
			}
			--m_itemsMin;
			--m_itemsMax;
		}
		return obj;
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && !m_isLocked && !m_isOpen;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// TODO: animations/delay/VFX?
		GetComponent<AudioSource>().PlayOneShot(m_openSFX.RandomWeighted());

		// update appearance/size
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		renderer.sprite = m_spriteOpen;
		renderer.size *= m_openSizePcts;
		BoxCollider2D collider = GetComponent<BoxCollider2D>();
		float heightPrev = collider.size.y;
		collider.size *= m_openSizePcts;
		collider.offset = new(collider.offset.x, collider.offset.y - heightPrev * 0.5f + collider.size.y * 0.5f); // TODO: don't assume that the space below the collider should stay the same height?

		// ensure children are active
		for (int i = 0, n = transform.childCount; i < n; ++i)
		{
			transform.GetChild(i).gameObject.SetActive(true);
		}

		// activate any pre-spawned objects
		foreach (GameObject obj in m_prespawned)
		{
			if (obj == null) // NOTE that this is possible if locks have been unlocked via other means (e.g. guesswork, console command)
			{
				continue;
			}
			obj.SetActive(true);
		}
		m_prespawned.Clear();

		m_isOpen = true;
	}


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
		// prevent empty/invalid locked chests
		if (m_prespawned.Count <= 0 || m_keyPrefabs.Length <= 0) // NOTE that this assumes that a furniture object's keys are always spawned AFTER its items, which is the case at least currently (see RoomController.FinalizeRecursive())
		{
			Unlock(null, true);
			return;
		}

		m_keyObj = keyRooms.Random().SpawnKey(m_keyPrefabs.RandomWeighted(), 0.0f, true, IsCriticalPath);
		GetComponent<SpriteRenderer>().color = m_keyObj.GetComponent<SpriteRenderer>().color;
	}

	public bool IsValidNextKey(GameObject obj) => m_keyObj == obj;

	public bool Unlock(IKey key, bool silent = false)
	{
		Debug.Assert(key == null || IsValidNextKey(key.Component.gameObject));
		GetComponent<SpriteRenderer>().sprite = m_spriteUnlocked;
		m_isLocked = false;
		if (key != null)
		{
			key.Deactivate();
		}
		if (!silent)
		{
			GetComponent<AudioSource>().PlayOneShot(m_unlockSFX.RandomWeighted());
		}
		return true;
	}


	private void OnCollisionEnter2D(Collision2D collision)
	{
		foreach (IKey key in collision.collider.GetComponentsInChildren<IKey>(true))
		{
			if (IsValidNextKey(key.Component.gameObject))
			{
				Unlock(key);
				break;
			}
		}
	}
}
