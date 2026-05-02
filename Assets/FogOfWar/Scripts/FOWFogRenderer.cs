using UnityEngine;


public class FOWFogRenderer : MonoBehaviour
{
    private Material                                        material_;
    private GameObject                                      fog_plane_;
    private FOWManager                                      manager_;
    void Start()
    {
        manager_ = GetComponent<FOWManager>();
        fog_plane_ = transform.GetChild(0).gameObject;
        fog_plane_.transform.localPosition = Vector3.zero;
        fog_plane_.transform.localScale = new Vector3(manager_.HALF_LENGTH_X, 1, manager_.HALF_LENGTH_Y);
        material_ = fog_plane_.GetComponentInChildren<Renderer>().material;
    }

    void Update()
    {
        if (manager_.map_ == null)
            return;

        if (manager_.map_.CurrentTexture != null)
        {
            material_.SetTexture("_MainTex", manager_.map_.CurrentTexture);
        }
    }
}
