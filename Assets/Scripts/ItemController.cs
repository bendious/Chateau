using Platformer.Gameplay;
using Platformer.Mechanics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(VisualEffect)), RequireComponent(typeof(Collider2D), typeof(SpriteRenderer))]
public class ItemController : MonoBehaviour
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
	public float m_damage = 1.0f;
	public int m_healAmount = 0;

	public AudioClip[] m_swingThrowAudio;
	public AudioClip[] m_collisionAudio;


	public float Speed => m_arm == null ? m_body.velocity.magnitude : m_arm.Speed;

	public Vector2 SpritePivotOffset => -(m_renderer.sprite.pivot / m_renderer.sprite.rect.size * 2.0f - Vector2.one) * m_renderer.sprite.bounds.extents;


	private Rigidbody2D m_body;
	private VisualEffect m_vfx;
	private AudioSource m_audioSource;
	private Collider2D m_collider;
	private SpriteRenderer m_renderer;
	private Health m_health;

	private ArmController m_arm;
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
		m_collider = GetComponent<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();

		m_arm = transform.parent == null ? null : transform.parent.GetComponent<ArmController>();
		SetCause(m_arm == null ? null : m_arm.transform.parent.gameObject);
		Vector3 size = m_collider.bounds.size;
		m_vfx.SetFloat("Size", Mathf.Max(size.x, size.y));
		m_vfx.SetVector3("SpriteOffset", SpritePivotOffset);
	}

	// TODO: only when VFX is enabled?
	private void FixedUpdate()
	{
		if (Speed >= m_damageThresholdSpeed)
		{
			m_vfx.SetVector3(m_posLocalPrevID, Quaternion.Inverse(transform.rotation) * -(Vector3)m_body.velocity * Time.fixedDeltaTime + Vector3.forward); // NOTE the inclusion of Vector3.forward to put the VFX in the background // TODO: don't assume constant/unchanged velocity across the time step?
		}
		else
		{
			m_vfx.Stop();
			StopAllCoroutines();
			if (m_arm == null)
			{
				SetCause(null);
			}
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}


	public void AttachTo(ArmController arm)
	{
		m_arm = arm;
		arm.OnItemAttachment(this); // NOTE that this needs to be BEFORE changing the item transform

		transform.SetParent(arm.transform);
		transform.localPosition = Vector3.right * arm.GetComponent<SpriteRenderer>().sprite.bounds.size.x; // TODO: lerp?
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		m_body.velocity = Vector2.zero;
		m_body.angularVelocity = 0.0f;
		m_body.bodyType = RigidbodyType2D.Kinematic;
		gameObject.layer = arm.gameObject.layer;
		SetCause(arm.transform.parent.gameObject);
	}

	public void Detach()
	{
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		m_body.bodyType = RigidbodyType2D.Dynamic;
	}

	public void Swing()
	{
		m_arm.Swing(m_swingDegreesPerSec, m_swingRadiusPerSec, m_radiusSpringStiffness, m_radiusSpringDampPct);

		EnableVFX();

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
			bool healed = m_arm.transform.parent.GetComponent<Health>().Increment(m_healAmount);
			if (healed)
			{
				transform.parent = null; // so that we can refresh inventory immediately even though object deletion is deferred
				Destroy(gameObject);
				return true;
			}
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		EnableCollision.TemporarilyDisableCollision(m_arm.transform.parent.GetComponent<Collider2D>(), m_collider, 0.1f);

		Detach();
		m_body.velocity = transform.rotation * Vector2.right * m_throwSpeed;

		EnableVFX();

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}


	private void ProcessCollision(Collision2D collision)
	{
		// TODO: flag/unflag within the physics system itself?
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		if (kinematicObj != null && kinematicObj.ShouldIgnore(m_body, m_collider, true, false))
		{
			return;
		}

		// maybe attach to character
		bool isDetached = m_arm == null;
		bool causeCanDamage = m_cause != null && m_cause != collision.gameObject; // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus
		if (isDetached && !causeCanDamage)
		{
			AnimationController character = collision.gameObject.GetComponent<AnimationController>();
			if (character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>().Length < character.m_maxPickUps)
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

	private void SetCause(GameObject cause)
	{
		if (m_cause == cause)
		{
			return;
		}

		m_cause = cause;

		if (m_cause == null)
		{
			return;
		}

		Gradient gradient = new();
		gradient.colorKeys = new GradientColorKey[] { new(cause == Camera.main.GetComponent<GameController>().m_avatar.gameObject ? Color.white : Color.red, 0.0f) };
		gradient.alphaKeys = new GradientAlphaKey[] { new(0.0f, 0.0f), new(m_vfxAlphaMax, 1.0f) }; // TODO: determine how this interacts w/ the VFX's Alpha Over Life node
		m_vfx.SetGradient(m_gradientID, gradient);
	}

	private void EnableVFX()
	{
		m_vfx.enabled = true;
		m_vfx.Play();
		StopAllCoroutines();
		StartCoroutine(UpdateVFX());
	}

	private IEnumerator UpdateVFX()
	{
		while (true)
		{
			m_vfx.SetVector3(m_upVecID, transform.rotation * Vector3.up);
			yield return null;
		}
	}
}
