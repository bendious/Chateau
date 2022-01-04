using UnityEngine;


public /*static*/ class ConsoleCommands : MonoBehaviour
{
    public static bool PassiveAI { get; private set; }


#if DEBUG
    // TODO: callback rather than checking every frame?
    private void Update()
    {
        if (!Input.GetKey(KeyCode.BackQuote))
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            PassiveAI = !PassiveAI;
        }

        if (Input.GetKeyDown(KeyCode.W))
        {
            Camera.main.GetComponent<GameController>().SpawnEnemyWave();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            Camera.main.GetComponent<GameController>().DebugKillAllEnemies();
        }
    }
#endif
}
