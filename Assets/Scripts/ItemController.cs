using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(Collider2D)), RequireComponent(typeof(SpriteRenderer))]
public sealed class ItemController : MonoBehaviour, IInteractable
{
	public float m_swingDegreesPerSec = 5000.0f;
	public float m_swingRadiusPerSec = 10.0f;
	public float m_aimSpringStiffness = 100.0f;
	public float m_aimSpringDampPct = 0.5f;
	public float m_radiusSpringStiffness = 25.0f;
	public float m_radiusSpringDampPct = 0.5f;
	public float m_damageThresholdSpeed = 2.0f;
	public float m_throwSpeed = 10.0f;
	public float m_vfxAlphaMax = 0.35f;
	public Vector3 m_vfxExtraOffsetLocal;
	public float m_damage = 1.0f;
	public int m_healAmount = 0;
	public string m_overlayText = null;

	public AudioClip[] m_swingThrowAudio;
	public AudioClip[] m_collisionAudio;


	public /*override*/ GameObject Object => gameObject;

	public float Speed => m_holder == null ? m_body.velocity.magnitude : m_holder.Speed;

	public Vector2 SpritePivotOffset => -(m_renderer.sprite.pivot / m_renderer.sprite.rect.size * 2.0f - Vector2.one) * m_renderer.sprite.bounds.extents;


	private Rigidbody2D m_body;
	private VisualEffect m_vfx;
	private AudioSource m_audioSource;
	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private Health m_health;

	private IHolder m_holder;
	private GameObject m_cause;

	private static int m_posLocalPrevID;
	private static int m_upVecID;
	private static int m_gradientID;


	private void Awake()
	{
		m_posLocalPrevID = Shader.PropertyToID("PosLocalPrev");
		m_upVecID = Shader.PropertyToID("UpVec");
		m_gradientID = Shader.PropertyToID("Gradient");

		m_body = GetComponent<Rigidbody2D>();
		m_vfx = GetComponent<VisualEffect>();
		m_audioSource = GetComponent<AudioSource>();
		m_colliders = GetComponents<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();

		m_holder = transform.parent == null ? null : transform.parent.GetComponent<IHolder>();
#pragma warning disable IDE0031 // NOTE that we don't use null propagation since IHolderControllers can be Unity objects as well, which don't like ?? or ?.
		SetCause(m_holder == null ? null : m_holder.Object.transform.parent.gameObject);
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
		if (m_vfx != null && Speed >= m_damageThresholdSpeed)
		{
			m_vfx.SetVector3(m_posLocalPrevID, Quaternion.Inverse(transform.rotation) * -(Vector3)m_body.velocity * Time.fixedDeltaTime + Vector3.forward); // NOTE the inclusion of Vector3.forward to put the VFX in the background // TODO: don't assume constant/unchanged velocity across the time step?
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		if (kinematicObj != null && kinematicObj.ShouldIgnore(m_body, m_colliders, true, false))
		{
			return;
		}

		// maybe attach to character
		// TODO: extend to BackpackController as well?
		bool isDetached = m_holder == null;
		bool causeCanDamage = m_cause != null && m_cause != collision.gameObject; // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus
		if (isDetached && !causeCanDamage)
		{
			KinematicCharacter character = collision.gameObject.GetComponent<KinematicCharacter>();
			if (character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>().Length < character.MaxPickUps)
			{
				character.AttachItem(this);
				AvatarController avatar = collision.gameObject.GetComponent<AvatarController>();
				if (avatar != null)
				{
					avatar.InventorySync();
				}
				return;
			}
		}

		// check speed
		float collisionSpeed = kinematicObj == null ? collision.relativeVelocity.magnitude : (m_body.velocity - kinematicObj.velocity).magnitude + Speed;
		if (collisionSpeed > m_damageThresholdSpeed)
		{
			// play audio
			if (m_collisionAudio != null && m_collisionAudio.Length > 0 && m_audioSource.enabled)
			{
				m_audioSource.PlayOneShot(m_collisionAudio[Random.Range(0, m_collisionAudio.Length)]);
			}

			// if from a valid source, apply damage
			if (causeCanDamage)
			{
				Health otherHealth = collision.gameObject.GetComponent<Health>();
				if (otherHealth != null)
				{
					otherHealth.Decrement(gameObject, m_damage); // TODO: round if damaging avatar?
				}
				if (m_health != null)
				{
					m_health.Decrement(gameObject);
				}
			}
		}

		// done for fully-dynamic collisions
		if (kinematicObj == null)
		{
			return;
		}

		// add upward force to emulate kicking
		if (isDetached && (m_cause == null || collision.gameObject == Camera.main.GetComponent<GameController>().m_avatar.gameObject))
		{
			SetCause(collision.gameObject);
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
			Detach();
		}
		m_holder = holder;

		transform.SetParent(holder.Object.transform);
		transform.localPosition = holder.ChildAttachPointLocal; // TODO: lerp?
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		m_body.velocity = Vector2.zero;
		m_body.angularVelocity = 0.0f;
		m_body.bodyType = RigidbodyType2D.Kinematic;
		gameObject.layer = holder.Object.layer;
		SetCause(holder.Object.transform.parent.gameObject);
	}

	// this is the detachment entry point
	public void Detach()
	{
		m_holder.ItemDetach(this);
	}

	// this (although public) should only be called by IHolderController.ItemDetachInternal() // TODO?
	public void DetachInternal()
	{
		// TODO: combine w/ BackpackController.Detach()?
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		m_body.bodyType = RigidbodyType2D.Dynamic;
		m_body.WakeUp();

		m_holder = null;
	}

	public void Swing()
	{
		if (m_holder is ArmController arm)
		{
			arm.Swing(m_swingDegreesPerSec, m_swingRadiusPerSec, m_radiusSpringStiffness, m_radiusSpringDampPct);
		}

		EnableVFXAndDamage();

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}

	public bool Use()
	{
		if (m_healAmount > 0)
		{
			bool healed = m_holder.Object.transform.parent.GetComponent<Health>().Increment(m_healAmount);
			if (healed)
			{
				Detach(); // so that we can refresh inventory immediately even though object deletion is deferred
				Destroy(gameObject);
				return true;
			}
		}

		if (!string.IsNullOrEmpty(m_overlayText))
		{
			Camera.main.GetComponent<GameController>().ToggleOverlay(m_renderer, m_overlayText);
			return true;
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		EnableCollision.TemporarilyDisableCollision(m_holder.Object.transform.parent.GetComponents<Collider2D>(), m_colliders, 0.1f);

		Detach();
		m_body.velocity = transform.rotation * Vector2.right * m_throwSpeed;

		EnableVFXAndDamage();

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}


	private void SetCause(GameObject cause)
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
		gradient.colorKeys = new GradientColorKey[] { new(cause == Camera.main.GetComponent<GameController>().m_avatar.gameObject ? Color.white : Color.red, 0.0f) };
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
			if (Speed >= m_damageThresholdSpeed)
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
				StopAllCoroutines();
				break;
			}
			yield return null;
		}
	}
}
