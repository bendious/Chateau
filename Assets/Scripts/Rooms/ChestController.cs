using System.Collections.Generic;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(AudioSource))]
public class ChestController : FurnitureController, IInteractable, IUnlockable
{
	[SerializeField] private WeightedObject<GameObject>[] m_keyPrefabs;
	[SerializeField] private WeightedObject<AudioClip>[] m_unlockSFX;
	[SerializeField] private Sprite m_spriteUnlocked;
	[SerializeField] private Sprite m_spriteOpen; // TODO: use animation?
	[SerializeField] private WeightedObject<AudioClip>[] m_openCloseSFX;
	[SerializeField] private float m_openCloseDamage;
	[SerializeField] private Health.DamageType m_damageType = Health.DamageType.Blunt;
	[SerializeField] private Vector2 m_openSizePcts = new(1.0f, 0.5f);
	[SerializeField] private float m_reuseSeconds = -1.0f;


	public GameObject Parent { get => null; set => Debug.LogError("Cannot set ChestController.Parent"); }
	public bool IsLocked => m_isLocked;

	public bool IsCriticalPath { private get; set; }


	private GameObject m_keyObj;
	private bool m_isLocked = true;
	private bool m_isOpen = false;
	private float m_lastOpenCloseTime = -1.0f;
	private readonly List<GameObject> m_enclosedObjects = new();


	public override List<GameObject> SpawnItems(bool rare, RoomType roomType, int itemCountExisting, int furnitureRemaining, List<GameObject> prefabsSpawned, float sizeScalarX = 1.0f, float sizeScalarY = 1.0f)
	{
		List<GameObject> items = base.SpawnItems(rare, roomType, itemCountExisting, furnitureRemaining, prefabsSpawned, Mathf.Min(1.0f, sizeScalarX * m_openSizePcts.x), Mathf.Min(1.0f, sizeScalarY * m_openSizePcts.y));
		if (!m_isOpen)
		{
			foreach (GameObject obj in items)
			{
				obj.SetActive(false);
				m_enclosedObjects.Add(obj);
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
			m_enclosedObjects.Add(obj);
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


	public bool CanInteract(KinematicCharacter interactor) => enabled && !m_isLocked && (m_reuseSeconds < 0.0f ? !m_isOpen : (m_lastOpenCloseTime + m_reuseSeconds <= Time.time));

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// TODO: animations/delay/VFX?
		GetComponent<AudioSource>().PlayOneShot(m_openCloseSFX.RandomWeighted());

		// activate any enclosed objects
		List<GameObject> enclosedPrev = new(m_enclosedObjects); // NOTE the copy due to m_enclosedObjects.Clear() below
		if (!m_isOpen)
		{
			foreach (GameObject obj in m_enclosedObjects)
			{
				if (obj == null) // NOTE that this is possible if locks have been unlocked via other means (e.g. guesswork, console command)
				{
					continue;
				}
				obj.SetActive(true);
			}
			m_enclosedObjects.Clear();
		}

		// damage anything in contact with us
		if (m_openCloseDamage > 0.0f)
		{
			if (m_isOpen)
			{
				// damage immediately, BEFORE disabling colliders
				ContactProcessing(m_isOpen, interactor.gameObject, enclosedPrev);
			}
			else
			{
				// wait to damage until colliders are active and have updated contacts
				StartCoroutine(ContactProcessingDelayed(m_isOpen, interactor.gameObject, enclosedPrev));
			}
		}

		// update appearance/size
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		renderer.sprite = m_isOpen ? m_spriteUnlocked : m_spriteOpen;
		renderer.size = m_isOpen ? renderer.size / m_openSizePcts : renderer.size * m_openSizePcts;
		BoxCollider2D collider = GetComponent<BoxCollider2D>();
		float heightPrev = collider.size.y;
		collider.size = m_isOpen ? collider.size / m_openSizePcts : collider.size * m_openSizePcts;
		collider.offset = new(collider.offset.x, collider.offset.y - heightPrev * 0.5f + collider.size.y * 0.5f); // TODO: don't assume that the space below the collider should stay the same height?

		// toggle child activations
		for (int i = 0, n = transform.childCount; i < n; ++i)
		{
			transform.GetChild(i).gameObject.SetActive(!m_isOpen);
		}

		m_isOpen = !m_isOpen;
		m_lastOpenCloseTime = Time.time;
	}


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
		// prevent empty/invalid locked chests
		if (m_enclosedObjects.Count <= 0 || m_keyPrefabs.Length <= 0) // NOTE that this assumes that a furniture object's keys are always spawned AFTER its items, which is the case at least currently (see RoomController.FinalizeRecursive())
		{
			Unlock(null, true);
			return;
		}

		RoomController keyRoomSafe = keyRooms == null || keyRooms.Length <= 0 ? lockRoom : keyRooms.Random();
		if (keyRoomSafe == null)
		{
			Debug.LogWarning("No rooms given when spawning keys?");
			return;
		}
		m_keyObj = keyRoomSafe.SpawnKey(m_keyPrefabs.RandomWeighted(), 0.0f, true, IsCriticalPath);
		GetComponent<SpriteRenderer>().color = m_keyObj.GetComponent<SpriteRenderer>().color;
	}

	public bool IsValidNextKey(GameObject obj) => m_keyObj == obj && (!obj.TryGetComponent(out ItemController item) || (item.Cause != null && item.Cause is AvatarController));

	public bool Unlock(IKey key, bool silent = false)
	{
		Debug.Assert(key == null || IsValidNextKey(key.Component.gameObject));
		GetComponent<SpriteRenderer>().sprite = m_spriteUnlocked;
		m_isLocked = false;
		key?.Deactivate();
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


	private void ContactProcessing(bool isClosing, GameObject source, List<GameObject> ignoredObjects)
	{
		Debug.Assert(m_openCloseDamage > 0.0f); // TODO: decouple enclosing from damage capability?

		List<ContactPoint2D> contacts = new();
		Bounds boundsOverall = GetComponent<SpriteRenderer>().bounds; // TODO: use aggregate collider bounds?
		foreach (Collider2D c in GetComponentsInChildren<Collider2D>())
		{
			c.GetContacts(contacts);
			foreach (ContactPoint2D contact in contacts)
			{
				Rigidbody2D body = contact.rigidbody;
				if (body == null)
				{
					continue; // TODO: support static objects?
				}
				if (body.gameObject == source || ignoredObjects.Contains(body.gameObject) || body.transform.parent != null) // TODO: handle multi-part objects?
				{
					continue;
				}
				Bounds bounds = contact.collider.bounds; // TODO: handle multi-collider objects?
				if (isClosing && boundsOverall.min.x <= bounds.min.x && boundsOverall.min.y <= bounds.min.y && boundsOverall.max.x >= bounds.max.x && boundsOverall.max.y >= bounds.max.y)
				{
					// fully enclosed; shut inside
					body.gameObject.SetActive(false);
					m_enclosedObjects.Add(body.gameObject);
				}
				else
				{
					// across the edge; apply damage
					Health health = contact.collider.ToHealth(); // NOTE that it's okay if we get duplicate health components since Health.Decrement() should handle aggregating into a single damage event
					if (health != null)
					{
						health.Decrement(source, gameObject, m_openCloseDamage, m_damageType);
					}
				}
			}
		}
	}

	private System.Collections.IEnumerator ContactProcessingDelayed(bool isClosing, GameObject source, List<GameObject> ignoredObjects)
	{
		yield return null;
		ContactProcessing(isClosing, source, ignoredObjects);
	}
}
