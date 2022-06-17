using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(Collider2D)), RequireComponent(typeof(SpriteRenderer))]
public sealed class ItemController : MonoBehaviour, IInteractable, IAttachable, IKey, ISavable
{
	[TextArea]
	public string m_tooltip;

	public SwingInfo m_swingInfo = new() {
		m_angularNewtonmeters = 150.0f,
		m_linearNewtons = 0.5f,
		m_aimSpringDampPct = 0.25f,
		m_radiusSpringDampPct = 0.5f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 1.0f
	};
	public float m_restDegreesOffset = 0.0f;
	public float m_throwSpeed = 20.0f;
	public float m_vfxAlpha = 0.5f;
	public int m_healAmount = 0;

	public bool m_detachOnDamage = false; // TODO: cumulative damage threshold?
	[SerializeField] private bool m_keyDestroyAfterUse = true;

	public Collider2D[] m_nondamageColliders;


	[SerializeField] private WeightedObject<string[]>[] m_sourceTextOptions;

	[SerializeField]
	private GameObject m_drawPrefab;


	public float Speed => m_holder == null ? m_body.velocity.magnitude : m_holder.Speed;

	public Vector3 TrailPosition => (m_trail == null ? transform : m_trail.transform).position;

	public bool IsSwinging => m_holder != null && m_holder.IsSwinging;


	private Rigidbody2D m_body;
	private TrailRenderer m_trail;
	private AudioSource m_audioSource;
	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private Health m_health;
	private Hazard m_hazard;

	private IHolder m_holder;
	public KinematicCharacter Cause { get; private set; }

	private GameObject m_drawObjectCurrent;


	[SerializeField]
	private int m_savableType = -1;
	int ISavable.Type { get => m_savableType; set => m_savableType = value; }

	public IUnlockable Lock { get; set; }
	public bool IsInPlace { get; set; }

	[SerializeField]
	private bool m_isReferenceKey;


	private void Awake()
	{
		m_body = GetComponent<Rigidbody2D>();
		m_trail = GetComponentInChildren<TrailRenderer>();
		m_audioSource = GetComponent<AudioSource>();
		m_colliders = GetComponents<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();
		m_hazard = GetComponent<Hazard>();

		m_holder = transform.parent == null ? null : transform.parent.GetComponent<IHolder>();
#pragma warning disable IDE0031 // NOTE that we don't use null propagation since IHolderControllers can be Unity objects as well, which don't like ?? or ?.
		SetCause(m_holder == null ? null : m_holder.Component.transform.root.GetComponent<KinematicCharacter>());
#pragma warning restore IDE0031
		if (m_trail != null)
		{
			Bounds aggregateBounds = m_colliders.First().bounds; // NOTE that we avoid default-initialization in case the aggregate bounds shouldn't include the origin
			foreach (Collider2D collider in m_colliders)
			{
				aggregateBounds.Encapsulate(collider.bounds);
			}
			Vector3 size = aggregateBounds.size - (m_trail.transform.position - transform.position);
			m_trail.widthCurve = AnimationCurve.Constant(0.0f, 0.0f, Mathf.Max(size.x, size.y));
			m_trail.material.SetTexture("_MainTex", m_renderer.sprite.texture); // TODO: share w/ other same-texture items?
		}
	}

	private void OnEnable()
	{
		if (m_holder != null)
		{
			StartCoroutine(((IAttachable)this).MirrorParentAlphaCoroutine()); // TODO: move into IAttachable somehow?
		}

		// prevent stale VFX when re-enabling items
		if (m_trail != null)
		{
			m_trail.emitting = false;
		}

		if (m_isReferenceKey)
		{
			IsInPlace = true;
		}
	}

	// TODO: combine w/ ArmController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}


	public void Interact(KinematicCharacter interactor, bool reverse) => interactor.ChildAttach(this);

	// this (although public) should only be called by IHolderController.ItemAttachInternal() // TODO?
	public void AttachInternal(IHolder holder)
	{
		IAttachable.AttachInternalShared(this, holder, m_body);

		m_holder = holder;

		if (m_drawObjectCurrent != null)
		{
			// stop drawing to prevent lines getting disabled "within" backpacks
			m_drawObjectCurrent.transform.SetParent(null);
			m_drawObjectCurrent = null;
		}

		m_body.useFullKinematicContacts = true;

		if (m_hazard != null)
		{
			m_hazard.enabled = false;
		}

		SetCause(holder.Component.transform.root.GetComponent<KinematicCharacter>());
	}

	// this is the detachment entry point
	public void /*override*/ Detach(bool noAutoReplace)
	{
		// ensure our overlay doesn't get stuck on
		AvatarController avatar = Cause == null ? null : Cause.GetComponent<AvatarController>();
		if (avatar != null && avatar.m_overlayCanvas.gameObject.activeSelf)
		{
			avatar.ToggleOverlay(null, null);
		}

		if (m_holder != null) // NOTE that this is valid for items attached to static geometry rather than an IHolder
		{
			m_holder.ChildDetach(this, noAutoReplace);
		}

		m_body.useFullKinematicContacts = false;

		m_detachOnDamage = false;
		m_holder = null;

		if (gameObject.activeSelf)
		{
			EnableVFXAndDamage(); // mostly to prevent m_cause from remaining set and allowing damage if run into fast enough
		}
	}

	void IKey.Use()
	{
		IsInPlace = true;

		if (!m_keyDestroyAfterUse)
		{
			return;
		}

		if (m_holder != null)
		{
			Detach(false);
		}
		gameObject.SetActive(false);
	}

	public void Deactivate()
	{
		if (!m_keyDestroyAfterUse)
		{
			return;
		}

		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}

	void ISavable.SaveInternal(SaveWriter saveFile)
	{
	}

	void ISavable.LoadInternal(SaveReader saveFile)
	{
	}

	public void Swing(bool isRelease)
	{
		if (m_holder is ArmController arm)
		{
			arm.Swing(isRelease);
		}

		if (isRelease)
		{
			return;
		}

		EnableVFXAndDamage();

		PlayMovementAudio();
	}

	public bool Use(bool isPressed)
	{
		if (isPressed && m_healAmount > 0)
		{
			bool healed = Cause.GetComponent<Health>().Increment(m_healAmount);
			if (healed)
			{
				Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
				return true;
			}
		}

		TMP_Text text = GetComponentInChildren<TMP_Text>();
		if (text != null && !string.IsNullOrEmpty(text.text))
		{
			AvatarController avatar = Cause.GetComponent<AvatarController>();
			if (avatar.m_overlayCanvas.gameObject.activeSelf != isPressed)
			{
				avatar.ToggleOverlay(m_renderer, text.text);
				return true;
			}
		}

		if (isPressed && transform.childCount > 0)
		{
			GameObject childObj = transform.GetChild(0).gameObject;
			if (childObj.GetComponent<UnityEngine.Rendering.Universal.Light2D>() != null) // TODO: support other types of use-activated child objects? check secondary children?
			{
				childObj.SetActive(!childObj.activeSelf);
				return true;
			}
		}

		// TODO: limit "ink" to prevent too many objects?
		if (m_drawPrefab != null && isActiveAndEnabled)
		{
			if (isPressed && (m_drawObjectCurrent == null || m_drawObjectCurrent.transform.parent != transform))
			{
				m_drawObjectCurrent = Instantiate(m_drawPrefab, transform);
				m_drawObjectCurrent.transform.localPosition = new Vector3(m_renderer.localBounds.max.x, 0.0f, 0.0f);
			}
			else if (!isPressed && m_drawObjectCurrent != null)
			{
				m_drawObjectCurrent.transform.SetParent(null);
				m_drawObjectCurrent = null;
			}
			return true;
		}

		if (isPressed && m_hazard != null)
		{
			m_hazard.enabled = true;
			Detach(false);
			return true;
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ platforms/furniture and thrower
		gameObject.layer = Cause.m_layerIgnoreOneWay;
		EnableCollision.TemporarilyDisableCollision(Cause.GetComponentsInChildren<Collider2D>(), m_colliders, 0.1f);

		Quaternion holderRotation = m_holder.Component.transform.rotation;
		m_body.velocity = holderRotation * Vector2.right * m_throwSpeed;
		ArmController arm = (ArmController)m_holder; // NOTE that m_holder gets unset by Detach(), but PostThrow() needs to be called afterward
		Detach(false);
		arm.PostThrow();

		EnableVFXAndDamage();

		PlayMovementAudio();
	}

	public void MergeWithSourceText(string prepend, IEnumerable<string> mainElements, string append)
	{
		string[] sourceText = m_sourceTextOptions.RandomWeighted();
		string aggregateText = sourceText.Zip(mainElements, (source, piece) => source.Replace("#", piece.Trim())).Aggregate("", (a, b) => a + "\n" + b);
		GetComponentInChildren<TMP_Text>().text = prepend + aggregateText + append;
	}


	private void SetCause(KinematicCharacter cause)
	{
		if (Cause == cause)
		{
			return;
		}

		Cause = cause;

		if (Cause == null || m_trail == null)
		{
			return;
		}

		m_trail.colorGradient = new() { colorKeys = new GradientColorKey[] { new(GameController.Instance.m_avatars.Contains(Cause) ? Color.white : Color.red, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new(m_vfxAlpha, 0.0f) } }; // NOTE that we have to replace the whole gradient rather than just setting individual attributes due to the annoying way LineRenderer prevents those changes
	}

	private void ProcessCollision(Collision2D collision)
	{
		if (!gameObject.activeSelf) // e.g. we're a bomb that has just gone off
		{
			return;
		}

		GameObject mainObj = collision.rigidbody == null ? collision.gameObject : collision.rigidbody.gameObject;
		KinematicObject kinematicObj = mainObj.GetComponent<KinematicObject>();
		if ((kinematicObj != null && kinematicObj.ShouldIgnore(m_body, m_colliders, false, 0.0f, null)) || collision.collider.transform.root == transform.root)
		{
			return;
		}

		// ignore non-destructible static objects when held
		bool isDetached = m_holder == null;
		Health otherHealth = mainObj.GetComponent<Health>();
		if (!isDetached && otherHealth == null)
		{
			if (collision.rigidbody == null || collision.rigidbody.bodyType != RigidbodyType2D.Dynamic)
			{
				return;
			}
		}

		// maybe attach to character
		// TODO: extend to BackpackController as well?
		bool canDamage = Cause != null && Cause.CanDamage(mainObj) && !m_nondamageColliders.Contains(collision.otherCollider);
		KinematicCharacter character = kinematicObj as KinematicCharacter; // NOTE that this works since objects shouldn't ever have multiple different KinematicObject-derived components
		if (isDetached && !canDamage) // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus or if it is a secondary (non-damaging) collider making collision
		{
			if (character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>(true).Length < character.HoldCountMax)
			{
				character.ChildAttach(this);
				return;
			}
		}

		// check speed
		float collisionSpeed = (collision.relativeVelocity + (kinematicObj == null ? Vector2.zero : -kinematicObj.velocity)).magnitude + Speed;
		if (collisionSpeed > m_swingInfo.m_damageThresholdSpeed)
		{
			if (m_audioSource.enabled)
			{
				PhysicsMaterial2D material1 = collision.collider.sharedMaterial != null || collision.rigidbody == null ? collision.collider.sharedMaterial : collision.rigidbody.sharedMaterial;
				PhysicsMaterial2D material2 = collision.otherCollider.sharedMaterial != null || collision.otherRigidbody == null ? collision.otherCollider.sharedMaterial : collision.otherRigidbody.sharedMaterial;
				m_audioSource.PlayOneShot(GameController.Instance.m_materialSystem.PairBestMatch(material1, material2).RandomCollisionAudio());
			}

			// if from a valid source, apply damage/detachment
			if (canDamage)
			{
				if (otherHealth != null)
				{
					otherHealth.Decrement(Cause != null ? Cause.gameObject : gameObject, m_swingInfo.m_damage); // TODO: round if damaging avatar?
				}
				if (m_health != null)
				{
					m_health.Decrement(gameObject);
				}
				if (m_detachOnDamage)
				{
					Detach(true);
				}
				ItemController otherItem = collision.collider.GetComponent<ItemController>();
				if (otherItem != null && otherItem.m_detachOnDamage)
				{
					otherItem.Detach(true);
				}
			}
		}

		if (isDetached)
		{
			// set layer back to default to re-enable default collisions
			gameObject.layer = GameController.Instance.m_layerDefault;
		}

		// done for fully-dynamic collisions
		if (kinematicObj == null)
		{
			return;
		}

		// add upward force to emulate kicking
		if (isDetached && !canDamage && character != null && (Cause == null || GameController.Instance.m_avatars.Contains(character)))
		{
			SetCause(character);
			EnableVFXAndDamage(); // mostly to prevent m_cause from remaining set and allowing damage if run into fast enough
		}
		List<ContactPoint2D> contacts = new();
		int contactCount = collision.GetContacts(contacts);
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() can have null end entries
		{
			ContactPoint2D pos = contacts[i];
			m_body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}

	private void EnableVFXAndDamage()
	{
		if (m_trail != null)
		{
			m_trail.emitting = true;
		}
		StartCoroutine(UpdateVFXAndCause());
	}

	private IEnumerator UpdateVFXAndCause()
	{
		while (true)
		{
			if (!IsSwinging && Speed < m_swingInfo.m_damageThresholdSpeed)
			{
				if (m_trail != null)
				{
					m_trail.emitting = false;
				}
				if (m_holder == null)
				{
					SetCause(null);
				}
				break;
			}
			yield return null;
		}
	}

	private void PlayMovementAudio()
	{
		if (!m_audioSource.enabled)
		{
			return;
		}
		m_audioSource.PlayOneShot(GameController.Instance.m_materialSystem.Find(m_colliders.First().sharedMaterial).RandomMovementAudio()); // TODO: don't assume first collider is main material?
	}
}
