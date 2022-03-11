using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(Collider2D)), RequireComponent(typeof(SpriteRenderer))]
public sealed class ItemController : MonoBehaviour, IInteractable
{
	public SwingInfo m_swingInfo = new() {
		m_degreesPerSec = 5000.0f,
		m_radiusPerSec = 10.0f,
		m_aimSpringStiffness = 100.0f,
		m_aimSpringDampPct = 0.5f,
		m_radiusSpringStiffness = 25.0f,
		m_radiusSpringDampPct = 0.5f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 1.0f
	};
	public float m_throwSpeed = 10.0f;
	public float m_vfxAlphaMax = 0.35f;
	public Vector3 m_vfxExtraOffsetLocal;
	public int m_healAmount = 0;

	public Collider2D[] m_nondamageColliders;


	public float Speed => m_holder == null ? m_body.velocity.magnitude : m_holder.Speed;

	public Vector2 SpritePivotOffset => -(m_renderer.sprite.pivot / m_renderer.sprite.rect.size * 2.0f - Vector2.one) * m_renderer.sprite.bounds.extents;

	public bool IsSwinging { get; private set; }


	private Rigidbody2D m_body;
	private VisualEffect m_vfx;
	private AudioSource m_audioSource;
	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private Health m_health;

	private IHolder m_holder;
	private KinematicCharacter m_cause;

	private static int m_posLocalPrevID;
	private static int m_upVecID;
	private static int m_gradientID;

	private static int m_layerDefault;


	private void Awake()
	{
		m_posLocalPrevID = Shader.PropertyToID("PosLocalPrev");
		m_upVecID = Shader.PropertyToID("UpVec");
		m_gradientID = Shader.PropertyToID("Gradient");

		m_layerDefault = LayerMask.NameToLayer("Default");

		m_body = GetComponent<Rigidbody2D>();
		m_vfx = GetComponent<VisualEffect>();
		m_audioSource = GetComponent<AudioSource>();
		m_colliders = GetComponents<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();

		m_renderer.color = new Color(Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f)); // NOTE that this has to be in Awake() and not Start() since LockController re-assigns key color after spawning // TODO: more deliberate choice?

		m_holder = transform.parent == null ? null : transform.parent.GetComponent<IHolder>();
#pragma warning disable IDE0031 // NOTE that we don't use null propagation since IHolderControllers can be Unity objects as well, which don't like ?? or ?.
		SetCause(m_holder == null ? null : m_holder.Component.transform.parent.GetComponent<KinematicCharacter>());
#pragma warning restore IDE0031
		if (m_vfx != null)
		{
			Bounds aggregateBounds = m_colliders.First().bounds; // NOTE that we avoid default-initialization in case the aggregate bounds shouldn't include the origin
			foreach (Collider2D collider in m_colliders)
			{
				aggregateBounds.Encapsulate(collider.bounds);
			}
			Vector3 size = aggregateBounds.size - m_vfxExtraOffsetLocal;
			m_vfx.SetFloat("Size", Mathf.Max(size.x, size.y));
			m_vfx.SetVector3("SpriteOffset", SpritePivotOffset);
			m_vfx.SetVector3("ExtraOffsetLocal", m_vfxExtraOffsetLocal);
		}
	}

	// TODO: only when VFX is enabled?
	private void FixedUpdate()
	{
		if (m_vfx != null && Speed >= m_swingInfo.m_damageThresholdSpeed)
		{
			m_vfx.SetVector3(m_posLocalPrevID, Quaternion.Inverse(transform.rotation) * -(Vector3)m_body.velocity * Time.fixedDeltaTime + Vector3.forward); // NOTE the inclusion of Vector3.forward to put the VFX in the background // TODO: don't assume constant/unchanged velocity across the time step?
		}
	}

	// TODO: combine w/ ArmController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
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
		bool canDamage = m_cause != null && m_cause.CanDamage(collision.gameObject) && !m_nondamageColliders.Contains(collision.otherCollider);
		KinematicCharacter character = kinematicObj as KinematicCharacter; // NOTE that this works since objects shouldn't ever have multiple different KinematicObject-derived components
		if (isDetached && !canDamage) // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus or if it is a secondary (non-damaging) collider making contact
		{
			if (character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>(true).Length < character.MaxPickUps)
			{
				character.AttachItem(this);
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
		if (isDetached && !canDamage && character != null && (m_cause == null || GameController.Instance.m_avatars.Contains(character)))
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

		m_holder.ItemDetach(this, noAutoReplace);
	}

	// this (although public) should only be called by IHolderController.ItemDetachInternal() // TODO?
	public void DetachInternal()
	{
		// TODO: combine w/ BackpackController.Detach()?
		transform.SetParent(null);
		m_body.bodyType = RigidbodyType2D.Dynamic;
		m_body.useFullKinematicContacts = false;
		m_body.WakeUp();

		EnableVFXAndDamage(); // mostly to prevent m_cause from remaining set and allowing damage if run into fast enough

		m_holder = null;
	}

	public void Swing()
	{
		if (m_holder is ArmController arm)
		{
			arm.Swing();
		}

		IsSwinging = true;
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

		Detach(false);
		m_body.velocity = transform.rotation * Vector2.right * m_throwSpeed;

		EnableVFXAndDamage();

		PlayMovementAudio();
	}


	private void SetCause(KinematicCharacter cause)
	{
		if (m_cause == cause)
		{
			return;
		}

		m_cause = cause;

		if (m_cause == null || m_vfx == null)
		{
			return;
		}

		Gradient gradient = new();
		gradient.colorKeys = new GradientColorKey[] { new(GameController.Instance.m_avatars.Contains(m_cause) ? Color.white : Color.red, 0.0f) };
		gradient.alphaKeys = new GradientAlphaKey[] { new(0.0f, 0.0f), new(m_vfxAlphaMax, 1.0f) }; // TODO: determine how this interacts w/ the VFX's Alpha Over Life node
		m_vfx.SetGradient(m_gradientID, gradient);
	}

	private void EnableVFXAndDamage()
	{
		if (m_vfx != null)
		{
			m_vfx.enabled = true;
			m_vfx.Play();
		}
		StopAllCoroutines();
		StartCoroutine(UpdateVFXAndCause());
	}

	private IEnumerator UpdateVFXAndCause()
	{
		while (true)
		{
			if (Speed >= m_swingInfo.m_damageThresholdSpeed)
			{
				if (m_vfx != null)
				{
					m_vfx.SetVector3(m_upVecID, transform.rotation * Vector3.up);
				}
			}
			else
			{
				if (m_vfx != null)
				{
					m_vfx.Stop();
				}
				if (m_holder == null)
				{
					SetCause(null);
				}
				IsSwinging = false;
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
