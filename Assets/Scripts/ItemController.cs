using Platformer.Mechanics;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
public class ItemController : MonoBehaviour
{
	public float m_swingDegreesPerSec = 5000.0f;
	public float m_aimDampTime = 0.1f;
	public float m_damageThresholdSpeed = 9.0f;


	public bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegrees) < 0.0f; // TODO: efficiency?


	private float m_aimDegrees;
	private float m_aimVelocity;


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
		m_aimVelocity += LeftFacing ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
	}

	public void UpdateAim(Vector3 position, float radius)
	{
		m_aimDegrees = Mathf.SmoothDampAngle(m_aimDegrees, AimDegreesRaw(position), ref m_aimVelocity, m_aimDampTime);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees);
		transform.localPosition = transform.localRotation * Vector3.right * radius - Vector3.forward; // NOTE the negative Z in order to force rendering on top of our parent
		GetComponent<SpriteRenderer>().flipY = LeftFacing;
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
		PlayerController playerController = collision.gameObject.GetComponent<PlayerController>();
		if (playerController != null && playerController.IsPickingUp)
		{
			AttachTo(collision.gameObject);
			return;
		}

		// if hitting a valid point fast enough, apply damage
		float collisionSpeed = kinematicObj == null ? collision.relativeVelocity.magnitude : (kinematicObj.velocity - body.velocity).magnitude + Mathf.Abs(m_aimVelocity); // TODO: incorporate aim velocity direction?
		bool locationCanDamage = kinematicObj == null ? true : transform.position.y > collision.gameObject.transform.position.y; // if a character, ignore collisions w/ lower body
		if (locationCanDamage && collisionSpeed > m_damageThresholdSpeed)
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
}
