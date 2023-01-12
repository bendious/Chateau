using UnityEngine;


public class Spring : MonoBehaviour
{
    public float m_launchDistance;


    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (m_launchDistance == 0.0f || collision.rigidbody == null || collision.rigidbody.bodyType == RigidbodyType2D.Static || collision.transform.parent != null)
        {
            return;
        }

        // ignore collisions at base of spring
        // TODO: parameterize?
        Vector2 collisionPos = collision.GetContact(0).point; // TODO: handle multi-contacts?
        if (collisionPos.y < collision.otherCollider.bounds.center.y)
        {
            return;
        }

        // calculate launch velocity
        // TODO: take player input into account?
        float twoG = 2.0f * Physics2D.gravity.magnitude;
        float launchMagnitude = Mathf.Sqrt(twoG * m_launchDistance); // v0 = sqrt(2*g*y); https://math.stackexchange.com/questions/785375/calculate-initial-velocity-to-reach-height-y // TODO: don't assume pure vertical launch?
        Vector2 launchVel = transform.rotation * Vector2.up * launchMagnitude;

        switch (collision.rigidbody.bodyType)
        {
            case RigidbodyType2D.Dynamic:
                collision.rigidbody.AddForceAtPosition(launchVel, collisionPos); // NOTE that we don't take into account the mass of the object since dynamic launches don't need to be as precise and feel better when not mass-independent
                break;
            case RigidbodyType2D.Kinematic:
                float decayTime = -launchMagnitude / twoG; // since y = v0*t + g*t^2, y' = v = v0 + 2*g*t, so when v=0, t = -v0 / 2*g
                collision.rigidbody.GetComponent<KinematicObject>().Bounce(launchVel, decayTime, decayTime);
                break;
        }

        // TODO: animation/SFX/VFX
    }
}
