using Meowarts.Knead;
using UnityEngine;

// Demo: 1–7 bounce every prop, P / L switch poses, R back to rest, click a physics box to drop a ball on it.
public class KneadDemo : MonoBehaviour
{
    public KneadBox[] props;
    public KneadBox posed;

    static readonly KneadEffect[] Effects =
    {
        KneadEffect.Squish, KneadEffect.Stretch, KneadEffect.Expand, KneadEffect.Shrink,
        KneadEffect.Twist, KneadEffect.Bend, KneadEffect.Wobble,
    };

    void Update()
    {
        for (int i = 0; i < Effects.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                foreach (var box in props) box.Bounce(KneadTween.Preset(Effects[i]));

        if (Input.GetKeyDown(KeyCode.P)) posed.TweenToPose("Tall");
        if (Input.GetKeyDown(KeyCode.L)) posed.TweenToPose("Lean", 0.4f, KneadEase.Spring);
        if (Input.GetKeyDown(KeyCode.R)) posed.TweenToRest();

        if (Input.GetMouseButtonDown(0) &&
            Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit) &&
            hit.collider.GetComponent<KneadPhysicsBox>())
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.transform.position = hit.collider.transform.position + Vector3.up * 3f;
            ball.transform.localScale = Vector3.one * 0.4f;
            ball.AddComponent<Rigidbody>().mass = 1f;
            Destroy(ball, 8f);
        }
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 700, 90),
            "1–7: Squish, Stretch, Expand, Shrink, Twist, Bend, Wobble\n" +
            "P: pose \"Tall\"   L: pose \"Lean\"   R: rest\n" +
            "Click a box in the back row to drop a 1 kg ball on it");
    }
}
