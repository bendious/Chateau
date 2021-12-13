using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Mechanics;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
public class ItemController : MonoBehaviour
{
	public float m_swingDegreesPerSec = 5000.0f;
	public float m_swingSpringStiffness = 100.0f;
	public float m_damageThresholdSpeed = 2.0f;
	public float m_throwSpeed = 100.0f;


	public bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegrees) < 0.0f; // TODO: efficiency?


	private float m_aimDegrees;
	private float m_aimVelocity;
	private bool m_swingDirection;


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
		GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
		m_aimDegrees = AimDegreesRaw(transform.position);
		m_aimVelocity = 0.0f;
	}

	public void Detach()
	{
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
		m_aimVelocity = 0.0f;
	}

	public void Swing()
	{
		m_aimVelocity += m_swingDirection ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
		m_swingDirection = !m_swingDirection;
	}

	public void UpdateAim(Vector3 position, float radius)
	{
		m_aimDegrees = UnderdampedSpringAngle(m_aimDegrees, AimDegreesRaw(position), ref m_aimVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees);
		transform.localPosition = transform.localRotation * Vector3.right * radius - Vector3.forward; // NOTE the negative Z in order to force rendering on top of our parent
		GetComponent<SpriteRenderer>().flipY = LeftFacing;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		Collider2D collider = GetComponent<Collider2D>();
		Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
		Physics2D.IgnoreCollision(collider, parentCollider, true);
		EnableCollision evt = Simulation.Schedule<EnableCollision>(0.1f);
		evt.m_collider1 = collider;
		evt.m_collider2 = parentCollider;

		Detach();
		GetComponent<Rigidbody2D>().AddForce(Quaternion.Euler(0.0f, 0.0f, m_aimDegrees) * Vector2.right * m_throwSpeed);
	}


	private void ProcessCollision(Collision2D collision)
	{
		// TODO: flag/unflag within the physics system itself?
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		if (kinematicObj != null && kinematicObj.ShouldIgnore(body, GetComponent<Collider2D>(), true, false))
		{
			return;
		}

		// maybe attach to avatar
		AvatarController avatarController = collision.gameObject.GetComponent<AvatarController>();
		if (avatarController != null && avatarController.IsPickingUp)
		{
			AttachTo(collision.gameObject);
			return;
		}

		// if hitting a valid point fast enough, apply damage
		float collisionSpeed = kinematicObj == null ? collision.relativeVelocity.magnitude : (body.velocity - kinematicObj.velocity).magnitude + Mathf.Abs(m_aimVelocity); // TODO: incorporate aim velocity direction?
		bool canDamage = avatarController == null ? true : false; // TODO: base on what object threw us
		if (canDamage && collisionSpeed > m_damageThresholdSpeed)
		{
			Health otherHealth = collision.gameObject.GetComponent<Health>();
			if (otherHealth != null)
			{
				otherHealth.Decrement();
			}
			Health health = GetComponent<Health>();
			if (health != null)
			{
				health.Decrement();
			}
		}

		// done for fully-dynamic collisions
		if (kinematicObj == null)
		{
			return;
		}

		// add upward force to emulate kicking
		List<ContactPoint2D> contacts = new List<ContactPoint2D>();
		int contactCount = collision.GetContacts(contacts);
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() for some reason adds a bunch of null entries
		{
			ContactPoint2D pos = contacts[i];
			body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}

	private float AimDegreesRaw(Vector3 position)
	{
		Vector2 aimDiff = position - transform.parent.position;
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
	}

	private float UnderdampedSpringAngle(float degreesCurrent, float degreesTarget, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float mass = GetComponent<Rigidbody2D>().mass;
		float damping_factor = /*2.0f **/ Mathf.Sqrt(m_swingSpringStiffness * mass); // NOTE that we leave off the 2 since we want a slightly underdamped spring
		float degreesDiff = degreesTarget - degreesCurrent;
		while (Mathf.Abs(degreesDiff) > 180.0f)
		{
			degreesDiff -= degreesDiff < 0.0f ? -360.0f : 360.0f;
		}
		float force = m_swingSpringStiffness * degreesDiff - damping_factor * velocityCurrent;

		float accel = force / mass;
		velocityCurrent += accel * Time.deltaTime;

		return degreesCurrent + velocityCurrent * Time.deltaTime;
	}
}
