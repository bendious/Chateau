using UnityEngine;


public class KinematicDropper : KinematicObject
{
	protected override void ComputeVelocity()
	{
		velocity += gravityModifier * Time.deltaTime * Physics2D.gravity; // TODO: more nuance?
	}
}
