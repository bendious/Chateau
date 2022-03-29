using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(Collider2D)), RequireComponent(typeof(SpriteRenderer))]
public sealed class ItemController : MonoBehaviour, IInteractable, IAttachable
{
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

	public Collider2D[] m_nondamageColliders;

	public float m_colorMin = 0.5f;
	public float m_colorMax = 1.0f;


	public float Speed => m_holder == null ? m_body.velocity.magnitude : m_holder.Speed;

	public Vector3 TrailPosition => (m_trail == null ? transform : m_trail.transform).position;

	public bool IsSwinging => m_holder != null && m_holder.IsSwinging;


	private Rigidbody2D m_body;
	private TrailRenderer m_trail;
	private AudioSource m_audioSource;
	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private Health m_health;

	private IHolder m_holder;
	public KinematicCharacter Cause { get; private set; }

	private static int m_layerDefault;


	private void Awake()
	{
		m_layerDefault = LayerMask.NameToLayer("Default");

		m_body = GetComponent<Rigidbody2D>();
		m_trail = GetComponentInChildren<TrailRenderer>();
		m_audioSource = GetComponent<AudioSource>();
		m_colliders = GetComponents<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();

		m_renderer.color = Utility.ColorRandom(m_colorMin, m_colorMax); // NOTE that this has to be in Awake() and not Start() since LockController re-assigns key color after spawning // TODO: more deliberate choice?

		m_holder = transform.parent == null ? null : transform.parent.GetComponent<IHolder>();
#pragma warning disable IDE0031 // NOTE that we don't use null propagation since IHolderControllers can be Unity objects as well, which don't like ?? or ?.
		SetCause(m_holder == null ? null : m_holder.Component.transform.parent.GetComponent<KinematicCharacter>());
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
		// prevent stale VFX when re-enabling items
		if (m_trail != null)
		{
			m_trail.emitting = false;
		}
	}

	// TODO: combine w/ ArmController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (!gameObject.activeSelf) // e.g. we're a bomb that has just gone off
		{
			return;
		}

		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		if ((kinematicObj != null && kinematicObj.ShouldIgnore(m_body, m_colliders, false, false, false)) || collision.gameObject.transform.root == transform.root)
		{
			return;
		}

		// ignore non-destructible static objects when held
		bool isDetached = m_holder == null;
		Health otherHealth = collision.gameObject.GetComponent<Health>();
		if (!isDetached && otherHealth == null)
		{
			Rigidbody2D otherBody = collision.gameObject.GetComponent<Rigidbody2D>();
			if (otherBody == null || otherBody.bodyType != RigidbodyType2D.Dynamic)
			{
				return;
			}
		}

		// maybe attach to character
		// TODO: extend to BackpackController as well?
		bool canDamage = Cause != null && Cause.CanDamage(collision.gameObject) && !m_nondamageColliders.Contains(collision.otherCollider);
		KinematicCharacter character = kinematicObj as KinematicCharacter; // NOTE that this works since objects shouldn't ever have multiple different KinematicObject-derived components
		if (isDetached && !canDamage) // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus or if it is a secondary (non-damaging) collider making contact
		{
			if (character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>(true).Length < character.HoldCountMax)
			{
				character.ChildAttach(this);
				return;
			}
		}

		// check speed
		float collisionSpeed = (kinematicObj == null ? collision.relativeVelocity.magnitude : (m_body.velocity - kinematicObj.velocity).magnitude) + Speed;
		if (collisionSpeed > m_swingInfo.m_damageThresholdSpeed)
		{
			if (m_audioSource.enabled)
			{
				m_audioSource.PlayOneShot(GameController.Instance.m_materialSystem.PairBestMatch(collision.collider.sharedMaterial, collision.otherCollider.sharedMaterial).RandomCollisionAudio());
			}

			// if from a valid source, apply damage
			if (canDamage)
			{
				if (otherHealth != null)
				{
					otherHealth.Decrement(gameObject, m_swingInfo.m_damage); // TODO: round if damaging avatar?
				}
				if (m_health != null)
				{
					m_health.Decrement(gameObject);
				}
			}
		}

		if (isDetached)
		{
			// set layer back to default to re-enable default collisions
			gameObject.layer = m_layerDefault;
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
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() for some reason adds a bunch of null entries
		{
			ContactPoint2D pos = contacts[i];
			m_body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}


	public void Interact(KinematicCharacter interactor) => interactor.ChildAttach(this);

	// this (although public) should only be called by IHolderController.ItemAttachInternal() // TODO?
	public void AttachInternal(IHolder holder)
	{
		if (m_holder != null)
		{
			Detach(false);
		}
		m_holder = holder;

		Component holderComp = m_holder.Component;
		transform.SetParent(holderComp.transform);
		transform.localPosition = m_holder.ChildAttachPointLocal; // TODO: lerp?
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		m_body.velocity = Vector2.zero;
		m_body.angularVelocity = 0.0f;
		m_body.bodyType = RigidbodyType2D.Kinematic;
		m_body.useFullKinematicContacts = true;
		gameObject.layer = holderComp.gameObject.layer;
		SetCause(holderComp.transform.parent.GetComponent<KinematicCharacter>());
	}

	// this is the detachment entry point
	public void /*override*/ Detach(bool noAutoReplace)
	{
		// ensure our overlay doesn't get stuck on
		AvatarController avatar = m_holder.Component.transform.parent.GetComponent<AvatarController>();
		if (avatar != null && avatar.m_overlayCanvas.gameObject.activeSelf)
		{
			avatar.ToggleOverlay(null, null);
		}

		m_holder.ChildDetach(this, noAutoReplace);
	}

	// this (although public) should only be called by IHolderController.ItemDetachInternal() // TODO?
	public void DetachInternal()
	{
		// TODO: combine w/ BackpackController.Detach()?
		transform.SetParent(null);
		m_body.bodyType = RigidbodyType2D.Dynamic;
		m_body.useFullKinematicContacts = false;
		m_body.WakeUp();

		if (gameObject.activeSelf)
		{
			EnableVFXAndDamage(); // mostly to prevent m_cause from remaining set and allowing damage if run into fast enough
		}

		m_holder = null;
	}

	public void Swing(bool isRelease)
	{
		if (m_holder is ArmController arm)
		{
			arm.Swing(isRelease);
		}

		EnableVFXAndDamage();

		PlayMovementAudio();
	}

	public bool Use()
	{
		if (m_healAmount > 0)
		{
			bool healed = m_holder.Component.transform.parent.GetComponent<Health>().Increment(m_healAmount);
			if (healed)
			{
				Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
				return true;
			}
		}

		TMP_Text text = GetComponentInChildren<TMP_Text>();
		if (text != null && !string.IsNullOrEmpty(text.text))
		{
			m_holder.Component.transform.parent.GetComponent<AvatarController>().ToggleOverlay(m_renderer, text.text);
			return true;
		}

		if (transform.childCount > 0)
		{
			GameObject childObj = transform.GetChild(0).gameObject;
			childObj.SetActive(!childObj.activeSelf);
			return true;
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		EnableCollision.TemporarilyDisableCollision(m_holder.Component.transform.parent.GetComponents<Collider2D>(), m_colliders, 0.1f);

		Quaternion holderRotation = m_holder.Component.transform.rotation;
		Detach(false);
		m_body.velocity = holderRotation * Vector2.right * m_throwSpeed;

		EnableVFXAndDamage();

		PlayMovementAudio();
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

		Gradient gradient = new();
		gradient.colorKeys = new GradientColorKey[] { new(GameController.Instance.m_avatars.Contains(Cause) ? Color.white : Color.red, 0.0f) };
		gradient.alphaKeys = new GradientAlphaKey[] { new(m_vfxAlpha, 0.0f) };
		m_trail.colorGradient = gradient;
	}

	private void EnableVFXAndDamage()
	{
		if (m_trail != null)
		{
			m_trail.emitting = true;
		}
		StopAllCoroutines();
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
				StopAllCoroutines();
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
