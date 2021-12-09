using Platformer.Mechanics;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
public class ItemController : MonoBehaviour
{
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
	}

	public void Detach()
	{
		transform.SetParent(null);
		GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
	}


	private void ProcessCollision(Collision2D collision)
	{
		// maybe attach to avatar
		PlayerController playerController = collision.gameObject.GetComponent<PlayerController>();
		if (playerController != null && playerController.IsPickingUp)
		{
			AttachTo(collision.gameObject);
			return;
		}

		// don't specially process non-character collisions
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		if (kinematicObj == null)
		{
			return;
		}

		// add upward force to emulate kicking
		List<ContactPoint2D> contacts = new List<ContactPoint2D>();
		int contactCount = collision.GetContacts(contacts);
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		float collisionSpeed = (kinematicObj.velocity - body.velocity).magnitude; // NOTE that we can't use collision.relativeVelocity since it doesn't know KinematicObject.velocity
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() for some reason adds a bunch of null entries
		{
			ContactPoint2D pos = contacts[i];
			body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}
}
