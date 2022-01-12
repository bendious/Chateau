using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Mechanics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource))]
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
	public int m_healAmount = 0;

	public AudioClip[] m_swingThrowAudio;
	public AudioClip[] m_collisionAudio;


	public bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegrees) < 0.0f; // TODO: efficiency?


	private float m_aimDegrees;
	private float m_aimVelocity;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;

	private Rigidbody2D m_body;
	private VisualEffect m_vfx;
	private AudioSource m_audioSource;
	private Collider2D m_collider;
	private SpriteRenderer m_renderer;
	private Health m_health;

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

		SetCause(transform.parent == null ? null : transform.parent.gameObject);
		Vector3 size = m_collider.bounds.size;
		m_vfx.SetFloat("Size", Mathf.Max(size.x, size.y));
		m_vfx.SetVector3("SpriteOffset", -(m_renderer.sprite.pivot / m_renderer.sprite.rect.size * 2.0f - Vector2.one) * m_renderer.sprite.bounds.extents);
	}

	// TODO: only when VFX is enabled?
	private void FixedUpdate()
	{
		float speed = m_body.velocity.magnitude + Mathf.Abs(m_aimVelocity) + Mathf.Abs(m_aimRadiusVelocity); // TODO: incorporate aim velocity direction?
		if (speed >= m_damageThresholdSpeed)
		{
			m_vfx.SetVector3(m_posLocalPrevID, Quaternion.Inverse(transform.rotation) * -(Vector3)m_body.velocity * Time.fixedDeltaTime + Vector3.forward); // NOTE the inclusion of Vector3.forward to put the VFX in the background // TODO: don't assume constant/unchanged velocity across the time step?
		}
		else
		{
			m_vfx.Stop();
			StopAllCoroutines();
			if (transform.parent == null)
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


	public void AttachTo(GameObject obj)
	{
		transform.SetParent(obj.transform);
		m_body.velocity = Vector2.zero;
		m_body.bodyType = RigidbodyType2D.Kinematic;
		gameObject.layer = obj.layer;
		m_aimDegrees = AimDegreesRaw(transform.position);
		m_aimVelocity = 0.0f;
		m_aimRadiusVelocity = 0.0f;
		SetCause(obj);
	}

	public void Detach()
	{
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		m_body.bodyType = RigidbodyType2D.Dynamic;
		m_aimVelocity = 0.0f;
		m_aimRadiusVelocity = 0.0f;
	}

	public void Swing()
	{
		m_aimVelocity += m_swingDirection ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
		m_aimRadiusVelocity += m_swingRadiusPerSec;
		m_swingDirection = !m_swingDirection;

		EnableVFX();

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}

	public void UpdateAim(Vector3 position, float radius)
	{
		Assert.IsFalse(position.x == float.MaxValue || position.x == float.PositiveInfinity || position.x == float.NegativeInfinity || position.x == float.NaN || radius == float.MaxValue || radius == float.PositiveInfinity || radius == float.NegativeInfinity || radius == float.NaN); // TODO: prevent unbounded radius growth (caused by low framerate?)
		m_aimDegrees = DampedSpring(m_aimDegrees, AimDegreesRaw(position), m_aimSpringDampPct, true, m_aimSpringStiffness, ref m_aimVelocity);
		m_aimRadius = DampedSpring(m_aimRadius, radius, m_radiusSpringDampPct, false, m_radiusSpringStiffness, ref m_aimRadiusVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees);
		transform.localPosition = transform.localRotation * Vector3.right * m_aimRadius - Vector3.forward; // NOTE the negative Z in order to force rendering on top of our parent
		m_renderer.flipY = LeftFacing;
	}

	public bool Use()
	{
		if (m_healAmount > 0)
		{
			bool healed = transform.parent.GetComponent<Health>().Increment(m_healAmount);
			if (healed)
			{
				Destroy(gameObject);
				return true;
			}
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
		Physics2D.IgnoreCollision(m_collider, parentCollider, true);
		EnableCollision evt = Simulation.Schedule<EnableCollision>(0.1f);
		evt.m_collider1 = m_collider;
		evt.m_collider2 = parentCollider;

		Detach();
		m_body.velocity = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees) * Vector2.right * m_throwSpeed;

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
		bool isDetached = transform.parent == null;
		if (isDetached)
		{
			AnimationController character = collision.gameObject.GetComponent<AnimationController>();
			if (character != null && character.IsPickingUp && character.transform.childCount < character.m_maxPickUps)
			{
				AttachTo(collision.gameObject);
				AvatarController avatar = collision.gameObject.GetComponent<AvatarController>();
				if (avatar != null)
				{
					avatar.InventorySync();
				}
				return;
			}
		}

		// check speed
		float collisionSpeed = kinematicObj == null ? collision.relativeVelocity.magnitude : (m_body.velocity - kinematicObj.velocity).magnitude + Mathf.Abs(m_aimVelocity) + Mathf.Abs(m_aimRadiusVelocity); // TODO: incorporate aim velocity direction?
		if (collisionSpeed > m_damageThresholdSpeed)
		{
			// play audio
			if (m_collisionAudio != null && m_collisionAudio.Length > 0 && m_audioSource.enabled)
			{
				m_audioSource.PlayOneShot(m_collisionAudio[Random.Range(0, m_collisionAudio.Length)]);
			}

			// if hitting a valid point, apply damage
			bool canDamage = m_cause != null && m_cause != collision.gameObject;
			if (canDamage)
			{
				Health otherHealth = collision.gameObject.GetComponent<Health>();
				if (otherHealth != null)
				{
					otherHealth.Decrement();
				}
				if (m_health != null)
				{
					m_health.Decrement();
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
		List<ContactPoint2D> contacts = new List<ContactPoint2D>();
		int contactCount = collision.GetContacts(contacts);
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() for some reason adds a bunch of null entries
		{
			ContactPoint2D pos = contacts[i];
			m_body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}

	private float AimDegreesRaw(Vector3 position)
	{
		Vector2 aimDiff = position - transform.parent.position;
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
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

		Gradient gradient = new Gradient();
		gradient.colorKeys = new GradientColorKey[] { new GradientColorKey(cause == Camera.main.GetComponent<GameController>().m_avatar.gameObject ? Color.white : Color.red, 0.0f) };
		gradient.alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }; // NOTE that this gets overridden by the VFX's Alpha Over Life node
		m_vfx.SetGradient(m_gradientID, gradient);
	}

	private void EnableVFX()
	{
		m_vfx.enabled = true;
		m_vfx.Play();
		StopAllCoroutines();
		StartCoroutine(UpdateVFX());
	}

	private float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float mass = m_body.mass;
		float dampingFactor = 2.0f * Mathf.Sqrt(m_aimSpringStiffness * mass) * dampPct;
		float diff = target - current;
		while (isAngle && Mathf.Abs(diff) > 180.0f)
		{
			diff -= diff < 0.0f ? -360.0f : 360.0f;
		}
		float force = stiffness * diff - dampingFactor * velocityCurrent;

		float accel = force / mass;
		velocityCurrent += accel * Time.fixedDeltaTime;

		return current + velocityCurrent * Time.fixedDeltaTime;
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
