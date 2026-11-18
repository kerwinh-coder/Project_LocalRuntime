                if (assetMap.TryGetValue(renderer.sharedMesh, out replacement))
                    renderer.sharedMesh = replacement as Mesh;
            }
        }

        foreach (Material material in assetMap.Values.OfType<Material>())
        {
            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(propertyName);
                UnityEngine.Object replacement = null;
                bool mapped = texture != null && assetMap.TryGetValue(texture, out replacement);
                Debug.Log(
                    "[FGO RemapDiag][MaterialTexture] material=" + material.name +
                    " property=" + propertyName +
                    " before=" + (texture != null ? texture.name + "#" + texture.GetInstanceID() : "NULL") +
                    " mapped=" + mapped +
                    (mapped ? " after=" + replacement.name + "#" + replacement.GetInstanceID() : ""));
                if (mapped)
                    material.SetTexture(propertyName, replacement as Texture);
            }
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material sourceMaterial = materials[i];
                UnityEngine.Object replacement = null;
                bool mapped = sourceMaterial != null &&
                    assetMap.TryGetValue(sourceMaterial, out replacement);
                Debug.Log(
                    "[FGO RemapDiag][RendererMaterial] renderer=" + GetHierarchyPath(renderer.transform) +
                    " slot=" + i +
                    " before=" + (sourceMaterial != null ? sourceMaterial.name + "#" + sourceMaterial.GetInstanceID() : "NULL") +
                    " mapped=" + mapped +
                    (mapped ? " after=" + replacement.name + "#" + replacement.GetInstanceID() : ""));
                if (mapped)
                    materials[i] = replacement as Material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static string MakeSafeName(string value)