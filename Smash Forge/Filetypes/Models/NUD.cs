﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK;
using System.Windows.Forms;
using SALT.Graphics;

namespace Smash_Forge
{
    public class NUD : FileBase
    {
        public NUD()
        {
            if (!Runtime.shaders.ContainsKey("NUD"))
            {
                Shader nud = new Shader();
                nud.vertexShader(File.ReadAllText(MainForm.executableDir + "/lib/Shader/NUD_vs.txt"));
                nud.fragmentShader(File.ReadAllText(MainForm.executableDir + "/lib/Shader/NUD_fs.txt"));
                Runtime.shaders.Add("NUD", nud);
            }

            if (!Runtime.hasCheckedNUDShaderCompilation)
            {
                Runtime.shaders["NUD"].shaderCompilationWarningMessage("NUD");
                Runtime.hasCheckedNUDShaderCompilation = true;
            }

            GL.GenBuffers(1, out vbo_position);
            GL.GenBuffers(1, out ibo_elements);
            GL.GenBuffers(1, out ubo_bones);
            GL.GenBuffers(1, out vbo_select);
        }

        public NUD(string fname) : this()
        {
            Read(fname);
            PreRender();

            List<Mesh> unsortedMeshes = new List<Mesh>();
            foreach (Mesh m in mesh)
            {
                unsortedMeshes.Add(m);
                //List<Order> SortedList = objListOrder.OrderBy(o => o.OrderDate).ToList();
            }
            depthSortedList = unsortedMeshes.OrderBy(o => o.boundingBox[2]).ToList();
        }

        // gl buffer objects
        int vbo_position;
        int ibo_elements;
        int ubo_bones;
        int vbo_select;

        public const int SMASH = 0;
        public const int POKKEN = 1;
        public int type = SMASH;
        public int boneCount = 0;
        public bool hasBones = false;
        public List<Mesh> mesh = new List<Mesh>();
        public SortedList<float, Mesh> depthSortedMeshes = new SortedList<float, Mesh>();
        List<Mesh> depthSortedList = new List<Mesh>();
        public float[] param = new float[4];

        // xmb stuff
        public int lightSetNumber = 0;
        public int directUVTime = 0;
        public bool useDirectUVTime = false;
        public string modelType = "";

        public override Endianness Endian { get; set; }

        #region Rendering

        public void Destroy()
        {
            GL.DeleteBuffer(vbo_position);
            GL.DeleteBuffer(ibo_elements);
            GL.DeleteBuffer(ubo_bones);
            GL.DeleteBuffer(vbo_select);

            mesh.Clear();
        }

        public enum TextureFlags
        {
            Glow = 0x00000080,
            Shadow = 0x00000040,
            DummyRamp = 0x00000020,
            SphereMap = 0x00000010,
            StageAOMap = 0x00000008,
            RampCubeMap = 0x00000004,
            NormalMap = 0x00000002,
            DiffuseMap = 0x00000001
        }

        public enum DummyTextures
        {
            StageMapLow = 0x10101000,
            StageMapHigh = 0x10102000,
            DummyRamp =  0x10080000
        }


        public void PreRender()
        {
            for (int mes = mesh.Count - 1; mes >= 0; mes--)
            {
                Mesh m = mesh[mes];
                for (int pol = m.Nodes.Count - 1; pol >= 0; pol--)
                {
                    Polygon p = (NUD.Polygon)m.Nodes[pol];
                    p.PreRender();
                }
            }
        }

        public void Render(Matrix4 mvpMatrix, VBN vbn)
        {
            if (Runtime.renderBoundingBox)
            {
                DrawBoundingBoxes();
            }

            Shader shader = Runtime.shaders["NUD"];
            GL.UseProgram(shader.programID);

            int renderType = (int)Runtime.renderType;
            GL.Uniform1(shader.getAttribute("renderType"), renderType);

            GL.Uniform1(shader.getAttribute("renderLighting"), Runtime.renderMaterialLighting ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderVertColor"), Runtime.renderVertColor ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderNormal"), Runtime.renderAlpha ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderDiffuse"), Runtime.renderDiffuse ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderFresnel"), Runtime.renderFresnel ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderSpecular"), Runtime.renderSpecular ? 1 : 0);
            GL.Uniform1(shader.getAttribute("renderReflection"), Runtime.renderReflection ? 1 : 0);
            GL.Uniform1(shader.getAttribute("useNormalMap"), Runtime.renderNormalMap ? 1 : 0);

            {
                GL.ActiveTexture(TextureUnit.Texture10);
                GL.BindTexture(TextureTarget.TextureCubeMap, RenderTools.cubeMapHigh);
                GL.Uniform1(shader.getAttribute("cmap"), 10);
                GL.UniformMatrix4(shader.getAttribute("mvpMatrix"), false, ref mvpMatrix);

                if (vbn != null)
                {
                    Matrix4[] f = vbn.getShaderMatrix();

                    int maxUniformBlockSize = GL.GetInteger(GetPName.MaxUniformBlockSize);
                    int boneCount = vbn.bones.Count;
                    int dataSize = boneCount * Vector4.SizeInBytes * 4;

                    GL.BindBuffer(BufferTarget.UniformBuffer, ubo_bones);
                    GL.BufferData(BufferTarget.UniformBuffer, (IntPtr)(dataSize), IntPtr.Zero, BufferUsageHint.DynamicDraw);
                    GL.BindBuffer(BufferTarget.UniformBuffer, 0);

                    var blockIndex = GL.GetUniformBlockIndex(shader.programID, "bones");
                    GL.BindBufferBase(BufferRangeTarget.UniformBuffer, blockIndex, ubo_bones);

                    if (f.Length > 0)
                    {
                        GL.BindBuffer(BufferTarget.UniformBuffer, ubo_bones);
                        GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, (IntPtr)(f.Length * Vector4.SizeInBytes * 4), f);
                    }
                }

                shader.enableAttrib();
                Render(shader);
                shader.disableAttrib();
            }
        }

        private void DrawBoundingBoxes()
        {
            GL.UseProgram(0);
            GL.Color4(Color.GhostWhite);
            RenderTools.drawCubeWireframe(new Vector3(param[0], param[1], param[2]), param[3]);
            GL.Color4(Color.OrangeRed);
            foreach (NUD.Mesh mesh in mesh)
            {
                if (mesh.Checked)
                    RenderTools.drawCubeWireframe(new Vector3(mesh.boundingBox[0], mesh.boundingBox[1], mesh.boundingBox[2]), mesh.boundingBox[3]);
            }
        }

        public void SetPropertiesFromXMB(XMBFile xmb)
        {
            if (xmb != null)
            {
                foreach (XMBEntry entry in xmb.Entries)
                {
                    if (entry.Name == "model")
                        modelType = xmb.Values[entry.FirstPropertyIndex];

                    if (entry.Children.Count > 0)
                    {
                        foreach (XMBEntry value in entry.Children)
                        {
                            if (xmb.Values.Count >= value.FirstPropertyIndex)
                            {
                                if (value.Name == "lightset")
                                {
                                    int.TryParse(xmb.Values[value.FirstPropertyIndex], out lightSetNumber);
                                }

                                if (value.Name == "directuvtime")
                                {
                                    useDirectUVTime = true;
                                    int.TryParse(xmb.Values[value.FirstPropertyIndex], out directUVTime);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void Render(Shader shader)
        {
            // create lists...
            // first draw opaque

            List<Polygon> opaque = new List<Polygon>();
            List<Polygon> trans = new List<Polygon>();

            foreach (Mesh m in depthSortedList)
            {
                Debug.WriteLine(m.boundingBox[2]);
                for (int pol = m.Nodes.Count - 1; pol >= 0; pol--)
                {
                    Polygon p = (Polygon)m.Nodes[m.Nodes.Count - 1 - pol];

                    if (p.materials.Count > 0)
                    {
                        if (p.materials[0].srcFactor != 0 || p.materials[0].dstFactor != 0)
                        {
                            trans.Add(p);
                            continue;
                        }
                    }
                   
                    opaque.Add(p);
                }
            }

            foreach (Polygon p in opaque)
                if (p.Parent != null && ((Mesh)p.Parent).Checked)
                    DrawPolygon(p, shader);

            foreach (Polygon p in trans)
                if (((Mesh)p.Parent).Checked)
                    DrawPolygon(p, shader);
            
            foreach (Mesh m in mesh)
            {
                for (int pol = m.Nodes.Count - 1; pol >= 0; pol--)
                {
                    Polygon p = (Polygon)m.Nodes[m.Nodes.Count - 1 - pol];
                    if (((Mesh)p.Parent).Checked)
                    {
                        if (Runtime.renderModelSelection && (((Mesh)p.Parent).IsSelected || p.IsSelected))
                        {
                            DrawPolygon(p, shader, true);
                        }
                    }
                }
            }
        }

        private void DrawPolygon(Polygon p, Shader shader, bool drawSelection = false)
        {
            if (p.faces.Count <= 3)
                return;

            Material mat = p.materials[0];


            //NSC
            GL.Uniform3(shader.getAttribute("NSC"), Vector3.Zero);
            if (p.Parent != null && p.Parent.Text.Contains("_NSC"))
            {
                int index = ((Mesh)p.Parent).singlebind;
                if (index != -1)
                    GL.Uniform3(shader.getAttribute("NSC"), Vector3.Transform(Vector3.Zero, Runtime.ModelContainers[0].vbn.bones[index].transform));
            }
            else
            {
                GL.Uniform3(shader.getAttribute("NSC"), Vector3.Zero);
            }


            GL.Uniform1(shader.getAttribute("flags"), mat.flags);
            GL.Uniform1(shader.getAttribute("isTransparent"), p.isTransparent ? 1 : 0);
            GL.Uniform1(shader.getAttribute("selectedBoneIndex"), Runtime.selectedBoneIndex);
            GL.Uniform1(shader.getAttribute("renderStageLighting"), Runtime.renderStageLighting ? 1 : 0);

            // shader uniforms
            SetTextureUniforms(shader, mat);
            SetMaterialPropertyUniforms(shader, mat);
            SetXMBUniforms(shader);

            // alpha blending
            GL.Enable(EnableCap.Blend);

            GL.BlendFunc(srcFactor.Keys.Contains(mat.srcFactor) ? srcFactor[mat.srcFactor] : BlendingFactorSrc.SrcAlpha,
                dstFactor.Keys.Contains(mat.dstFactor) ? dstFactor[mat.dstFactor] : BlendingFactorDest.OneMinusSrcAlpha);
            if (mat.srcFactor == 0 && mat.dstFactor == 0) GL.Disable(EnableCap.Blend);

            // alpha testing
            GL.Enable(EnableCap.AlphaTest);
            if (mat.AlphaTest == 0) GL.Disable(EnableCap.AlphaTest);

            float refAlpha = mat.RefAlpha / 255f;

            // gequal used because fragcolor.a of 0 is refalpha of 1
            GL.AlphaFunc(AlphaFunction.Gequal, 0.1f);
            switch (mat.AlphaFunc)
            {
                case 0x0:
                    GL.AlphaFunc(AlphaFunction.Never, refAlpha);
                    break;
                case 0x4:
                    GL.AlphaFunc(AlphaFunction.Gequal, refAlpha);
                    break;
                case 0x6:
                    GL.AlphaFunc(AlphaFunction.Gequal, refAlpha);
                    break;
            }

            // face culling
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Front);
            switch (mat.cullMode)
            {
                case 0:
                    GL.Disable(EnableCap.CullFace);
                    break;
                case 0x0205:
                    GL.CullFace(CullFaceMode.Front);
                    break;
                case 0x0405:
                    GL.CullFace(CullFaceMode.Back);
                    break;
            }

            SetVertexAttributes(p, shader);

            if (p.Checked)
            {
                if ((p.IsSelected || p.Parent.IsSelected) && drawSelection)
                {
                    DrawModelSelection(p, shader);
                }
                else
                {
                    if (Runtime.renderModelWireframe)
                    {
                        DrawModelWireframe(p, shader);
                    }

                    // need this
                    GL.DrawElements(PrimitiveType.Triangles, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);
                }
            }

        }

        private void SetXMBUniforms(Shader shader)
        {
            GL.Uniform1(shader.getAttribute("isStage"), modelType == "stage" ? 1 : 0);
            GL.Uniform1(shader.getAttribute("useDirectUVTime"), useDirectUVTime ? 1 : 0);
            GL.Uniform1(shader.getAttribute("lightSet"), lightSetNumber);
        }

        private void SetVertexAttributes(Polygon p, Shader shader)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo_position);
            GL.BufferData<dVertex>(BufferTarget.ArrayBuffer, (IntPtr)(p.vertdata.Length * dVertex.Size), p.vertdata, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(shader.getAttribute("vPosition"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 0);
            GL.VertexAttribPointer(shader.getAttribute("vNormal"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 12);
            GL.VertexAttribPointer(shader.getAttribute("vTangent"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 24);
            GL.VertexAttribPointer(shader.getAttribute("vBiTangent"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 36);
            GL.VertexAttribPointer(shader.getAttribute("vUV"), 2, VertexAttribPointerType.Float, false, dVertex.Size, 48);
            GL.VertexAttribPointer(shader.getAttribute("vColor"), 4, VertexAttribPointerType.Float, false, dVertex.Size, 56);
            GL.VertexAttribPointer(shader.getAttribute("vBone"), 4, VertexAttribPointerType.Float, false, dVertex.Size, 72);
            GL.VertexAttribPointer(shader.getAttribute("vWeight"), 4, VertexAttribPointerType.Float, false, dVertex.Size, 88);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo_elements);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(p.display.Length * sizeof(int)), p.display, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private static void DrawModelWireframe(Polygon p, Shader shader)
        {
            // use vertex color for wireframe color
            GL.Uniform1(shader.getAttribute("renderType"), (int)Runtime.RenderTypes.VertColor);
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Line);
            GL.LineWidth(2.0f);
            GL.DrawElements(PrimitiveType.Triangles, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            GL.Uniform1(shader.getAttribute("renderType"), (int)Runtime.renderType);
        }

        private static void DrawModelSelection(Polygon p, Shader shader)
        {
            GL.Enable(EnableCap.LineSmooth);
            GL.LineWidth(2.0f);

            bool[] cwm = new bool[4];
            GL.GetBoolean(GetIndexedPName.ColorWritemask, 4, cwm);
            GL.ColorMask(false, false, false, false);

            GL.StencilFunc(StencilFunction.Always, 1, 0xFF);
            GL.StencilMask(0xFF);

            GL.DrawElements(PrimitiveType.Triangles, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);

            GL.ColorMask(cwm[0], cwm[1], cwm[2], cwm[3]);

            GL.StencilFunc(StencilFunction.Notequal, 1, 0xFF);
            GL.StencilMask(0x00);

            // use vertex color for model selection color
            GL.Uniform1(shader.getAttribute("renderType"), (int)Runtime.RenderTypes.VertColor);
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Line);
            GL.LineWidth(2);
            GL.DrawElements(PrimitiveType.Triangles, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            GL.Uniform1(shader.getAttribute("renderType"), (int)Runtime.renderType);

            GL.StencilMask(0xFF);
            GL.Clear(ClearBufferMask.StencilBufferBit);
            GL.Enable(EnableCap.StencilTest);
        }

        private static void SetMaterialPropertyUniforms(Shader shader, Material mat)
        {
            // "NU_aoMinGain" becomes "aoMinGain"
            MatPropertyShaderUniform(shader, mat, "NU_aoMinGain", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_colorSamplerUV", 1, 1, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_colorSampler2UV", 1, 1, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_colorSampler3UV", 1, 1, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_colorGain", 1, 1, 1, 1);
            MatPropertyShaderUniform(shader, mat, "NU_finalColorGain", 1, 1, 1, 1);
            MatPropertyShaderUniform(shader, mat, "NU_colorOffset", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_diffuseColor", 1, 1, 1, 0.5f);
            MatPropertyShaderUniform(shader, mat, "NU_specularColor", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_specularColorGain", 1, 1, 1, 1);
            MatPropertyShaderUniform(shader, mat, "NU_specularParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_fresnelColor", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_fresnelParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_reflectionColor", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_reflectionParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_fogColor", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_fogParams", 0, 1, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_normalParams", 1, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_zOffset", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_effColorGain", 1, 1, 1, 1);
            MatPropertyShaderUniform(shader, mat, "NU_angleFadeParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_dualNormalScrollParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_normalSamplerAUV", 1, 1, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_alphaBlendParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_softLightingParams", 0, 0, 0, 0);
            MatPropertyShaderUniform(shader, mat, "NU_customSoftLightParams", 0, 0, 0, 0);

            // create some conditionals rather than using different shaders
            HasMatPropertyShaderUniform(shader, mat, "NU_softLightingParams", "hasSoftLight");
            HasMatPropertyShaderUniform(shader, mat, "NU_customSoftLightParams", "hasCustomSoftLight");
            HasMatPropertyShaderUniform(shader, mat, "NU_specularParams", "hasSpecularParams");
            HasMatPropertyShaderUniform(shader, mat, "NU_dualNormalScrollParams", "hasDualNormal");
        }

        private static void SetTextureUniforms(Shader shader, Material mat)
        {
            GL.Uniform1(shader.getAttribute("hasDif"), mat.diffuse ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasDif2"), mat.diffuse2 ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasDif3"), mat.diffuse3 ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasStage"), mat.stagemap ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasCube"), mat.cubemap ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasAo"), mat.aomap ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasNrm"), mat.normalmap ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasRamp"), mat.ramp ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasDummyRamp"), mat.dummyramp ? 1 : 0);
            GL.Uniform1(shader.getAttribute("hasColorGainOffset"), mat.useColorGainOffset ? 1 : 0);
            GL.Uniform1(shader.getAttribute("useDiffuseBlend"), mat.useDiffuseBlend ? 1 : 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, RenderTools.defaultTex);

            GL.ActiveTexture(TextureUnit.Texture10);
            GL.BindTexture(TextureTarget.Texture2D, RenderTools.UVTestPattern);
            GL.Uniform1(shader.getAttribute("UVTestPattern"), 10);

            GL.Uniform1(shader.getAttribute("dif"), 0);
            GL.Uniform1(shader.getAttribute("dif2"), 0);
            GL.Uniform1(shader.getAttribute("normalMap"), 0);
            GL.Uniform1(shader.getAttribute("cube"), 2);
            GL.Uniform1(shader.getAttribute("stagecube"), 2);
            GL.Uniform1(shader.getAttribute("spheremap"), 0);
            GL.Uniform1(shader.getAttribute("ao"), 0);
            GL.Uniform1(shader.getAttribute("ramp"), 0);

            int texid = 0;

            if (mat.diffuse && texid < mat.textures.Count)
            {
                int hash = mat.textures[texid].hash;
                if (mat.displayTexId != -1) hash = mat.displayTexId;
                GL.Uniform1(shader.getAttribute("dif"), BindTexture(mat.textures[texid], hash, texid));
                mat.diffuse1ID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.diffuse2 && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("dif2"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.diffuse2ID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.diffuse3 && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("dif3"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.diffuse3ID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.stagemap && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("stagecube"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.stageMapID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.cubemap && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("cube"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.cubeMapID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.spheremap && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("spheremap"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.sphereMapID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.aomap && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("ao"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.aoMapID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.normalmap && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("normalMap"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.normalID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.ramp && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("ramp"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.rampID = mat.textures[texid].hash;
                texid++;
            }
            if (mat.dummyramp && texid < mat.textures.Count)
            {
                GL.Uniform1(shader.getAttribute("dummyRamp"), BindTexture(mat.textures[texid], mat.textures[texid].hash, texid));
                mat.dummyRampID = mat.textures[texid].hash;
                texid++;
            }
        }

        private static void MatPropertyShaderUniform(Shader shader, Material mat, string propertyName, float default1,
            float default2, float default3, float default4)
        {
            float[] values;
            mat.entries.TryGetValue(propertyName, out values);
            if (mat.anims.ContainsKey(propertyName))
                values = mat.anims[propertyName];
            if (values == null)
                values = new float[] { default1, default2, default3, default4 };
            string uniformName = propertyName.Substring(3); // remove the NU_ from name

            try
            {
                GL.Uniform4(shader.getAttribute(uniformName), values[0], values[1], values[2], values[3]);
            }
            catch (System.IndexOutOfRangeException)
            {
                // something went wrong reading mat data somewhere...
                // some other part of the code will probably also fail
            }
        }

        public void MakeMetal(int newDiffuseID, bool preserveDiffuse, bool useNormalMap, float[] minGain, float[] refColor, float[] fresParams, float[] fresColor)
        {
            foreach (Mesh mesh in mesh)
            {
                foreach (Polygon poly in mesh.Nodes)
                {
                    foreach (Material mat in poly.materials)
                    {
                        float hash = -1f;
                        if (mat.entries.ContainsKey("NU_materialHash"))
                            hash = mat.entries["NU_materialHash"][0];

                        mat.anims.Clear();
                        mat.entries.Clear();

                        if (mat.normalmap && useNormalMap)
                            mat.flags = 0x9601106B;
                        else
                            mat.flags = 0x96011069;

                        int difTexID = 0;
                        if (preserveDiffuse)
                            difTexID = mat.diffuse1ID;
                        else
                            difTexID = newDiffuseID;

                        // add all the textures
                        mat.textures.Clear();
                        mat.displayTexId = -1;

                        NUD.Mat_Texture dif = NUD.Polygon.makeDefault();
                        dif.hash = difTexID; // preserve diffuse tex ID
                        NUD.Mat_Texture cub = NUD.Polygon.makeDefault();
                        cub.hash = 0x10102000;

                        NUD.Mat_Texture nrm = NUD.Polygon.makeDefault();
                        nrm.hash = mat.normalID; // preserve normal map tex ID. should work for all common texture flags

                        NUD.Mat_Texture rim = NUD.Polygon.makeDefault();
                        rim.hash = 0x10080000;

                        if (mat.normalmap)
                        {
                            mat.textures.Add(dif);
                            mat.textures.Add(cub);
                            mat.textures.Add(nrm);
                            mat.textures.Add(rim);
                        }
                        else
                            mat.textures.Add(dif);

                        mat.textures.Add(cub);
                        mat.textures.Add(rim);

                        // add material properties
                        mat.entries.Add("NU_colorSamplerUV", new float[] { 1, 1, 0, 0 });
                        mat.entries.Add("NU_fresnelColor", fresColor);
                        mat.entries.Add("NU_blinkColor", new float[] { 0f, 0f, 0f, 0 });
                        mat.entries.Add("NU_reflectionColor", refColor);
                        mat.entries.Add("NU_aoMinGain", minGain);
                        mat.entries.Add("NU_lightMapColorOffset", new float[] { 0f, 0f, 0f, 0 });
                        mat.entries.Add("NU_fresnelParams", fresParams);
                        mat.entries.Add("NU_alphaBlendParams", new float[] { 0f, 0f, 0f, 0 });
                        mat.entries.Add("NU_materialHash", new float[] { hash, 0f, 0f, 0 });
                    }
                }
            }
        }

        private static void HasMatPropertyShaderUniform(Shader shader, Material mat, string propertyName, string uniformName)
        {
            float[] values;
            mat.entries.TryGetValue(propertyName, out values);
            if (mat.anims.ContainsKey(propertyName))
                values = mat.anims[propertyName];

            int hasParam = 1;
            if (values == null)
                hasParam = 0;

            GL.Uniform1(shader.getAttribute(uniformName), hasParam);
        }

        
        public void RenderShadow(Matrix4 lightMatrix, Matrix4 view, Matrix4 modelMatrix)
        {
            // simple passthrough vertex render for shadow mapping
            Shader shader = Runtime.shaders["Shadow"];

            GL.UseProgram(shader.programID);

            GL.UniformMatrix4(shader.getAttribute("lightSpaceMatrix"), false, ref lightMatrix);
            GL.UniformMatrix4(shader.getAttribute("eyeview"), false, ref view);
            GL.UniformMatrix4(shader.getAttribute("modelMatrix"), false, ref modelMatrix);

            shader.enableAttrib();
            foreach(Mesh m in mesh)
            {
                foreach(Polygon p in m.Nodes)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo_position);
                    GL.BufferData<dVertex>(BufferTarget.ArrayBuffer, (IntPtr)(p.vertdata.Length * dVertex.Size), p.vertdata, BufferUsageHint.StaticDraw);
                    GL.VertexAttribPointer(shader.getAttribute("vPosition"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 0);

                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo_elements);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(p.display.Length * sizeof(int)), p.display, BufferUsageHint.StaticDraw);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

                    GL.DrawElements(PrimitiveType.Triangles, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);
                }
            }
            shader.disableAttrib();

            GL.UseProgram(0);
        }

        public void DrawPoints(Matrix4 mvpMatrix, VBN vbn)
        {
            Shader shader = Runtime.shaders["Point"];
            GL.UseProgram(shader.programID);
            GL.UniformMatrix4(shader.getAttribute("mvpMatrix"), false, ref mvpMatrix);
            GL.Uniform4(shader.getAttribute("color"), 1, 1, 1, 1);

            if (vbn != null)
            {
                Matrix4[] f = vbn.getShaderMatrix();

                int maxUniformBlockSize = GL.GetInteger(GetPName.MaxUniformBlockSize);
                int boneCount = vbn.bones.Count;
                int dataSize = boneCount * Vector4.SizeInBytes * 4;

                GL.BindBuffer(BufferTarget.UniformBuffer, ubo_bones);
                GL.BufferData(BufferTarget.UniformBuffer, (IntPtr)(dataSize), IntPtr.Zero, BufferUsageHint.DynamicDraw);
                GL.BindBuffer(BufferTarget.UniformBuffer, 0);

                var blockIndex = GL.GetUniformBlockIndex(shader.programID, "bones");
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, blockIndex, ubo_bones);

                if (f.Length > 0)
                {
                    GL.BindBuffer(BufferTarget.UniformBuffer, ubo_bones);
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, (IntPtr)(f.Length * Vector4.SizeInBytes * 4), f);
                }
            }

            shader.enableAttrib();
            foreach (Mesh m in mesh)
            {
                foreach (Polygon p in m.Nodes)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo_position);
                    GL.BufferData<dVertex>(BufferTarget.ArrayBuffer, (IntPtr)(p.vertdata.Length * dVertex.Size), p.vertdata, BufferUsageHint.StaticDraw);
                    GL.VertexAttribPointer(shader.getAttribute("vPosition"), 3, VertexAttribPointerType.Float, false, dVertex.Size, 0);
                    GL.VertexAttribPointer(shader.getAttribute("vBone"), 4, VertexAttribPointerType.Float, false, dVertex.Size, 72);
                    GL.VertexAttribPointer(shader.getAttribute("vWeight"), 4, VertexAttribPointerType.Float, false, dVertex.Size, 88);

                    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo_select);
                    GL.BufferData<int>(BufferTarget.ArrayBuffer, (IntPtr)(p.selectedVerts.Length * sizeof(int)), p.selectedVerts, BufferUsageHint.StaticDraw);
                    GL.VertexAttribPointer(shader.getAttribute("vSelected"), 1, VertexAttribPointerType.Int, false, sizeof(int), 0);

                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo_elements);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(p.display.Length * sizeof(int)), p.display, BufferUsageHint.StaticDraw);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                    
                    GL.PointSize(6f);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                    GL.DrawElements(PrimitiveType.Points, p.displayFaceSize, DrawElementsType.UnsignedInt, 0);
                }
            }
            shader.disableAttrib();
        }

        Dictionary<int, BlendingFactorDest> dstFactor = new Dictionary<int, BlendingFactorDest>(){
                    { 0x01, BlendingFactorDest.OneMinusSrcAlpha},
                    { 0x02, BlendingFactorDest.One},
                    { 0x03, BlendingFactorDest.OneMinusSrcAlpha},
                    { 0x04, BlendingFactorDest.OneMinusConstantAlpha},
                    { 0x05, BlendingFactorDest.ConstantAlpha},
        };

        static Dictionary<int, BlendingFactorSrc> srcFactor = new Dictionary<int, BlendingFactorSrc>(){
                    { 0x01, BlendingFactorSrc.SrcAlpha},
                    { 0x02, BlendingFactorSrc.SrcAlpha},
                    { 0x03, BlendingFactorSrc.SrcAlpha},
                    { 0x04, BlendingFactorSrc.SrcAlpha},
                    { 0x0a, BlendingFactorSrc.Zero}
        };

        static Dictionary<int, TextureWrapMode> wrapmode = new Dictionary<int, TextureWrapMode>(){
                    { 0x01, TextureWrapMode.Repeat},
                    { 0x02, TextureWrapMode.MirroredRepeat},
                    { 0x03, TextureWrapMode.ClampToEdge}
        };

        static Dictionary<int, TextureMinFilter> minfilter = new Dictionary<int, TextureMinFilter>(){
                    { 0x00, TextureMinFilter.LinearMipmapLinear},
                    { 0x01, TextureMinFilter.Nearest},
                    { 0x02, TextureMinFilter.Linear},
                    { 0x03, TextureMinFilter.NearestMipmapLinear},
        };

        static Dictionary<int, TextureMagFilter> magfilter = new Dictionary<int, TextureMagFilter>(){
                    { 0x00, TextureMagFilter.Linear},
                    { 0x01, TextureMagFilter.Nearest},
                    { 0x02, TextureMagFilter.Linear}
        };

        public static int BindTexture(NUD.Mat_Texture tex, int hash, int loc)
        {
            if (hash == (int) DummyTextures.StageMapLow)
            {
                GL.ActiveTexture(TextureUnit.Texture20 + loc);
                GL.BindTexture(TextureTarget.TextureCubeMap, RenderTools.cubeMapLow);
                return 20 + loc;

            }
            if (hash == (int)DummyTextures.StageMapHigh)
            {
                GL.ActiveTexture(TextureUnit.Texture20 + loc);
                GL.BindTexture(TextureTarget.TextureCubeMap, RenderTools.cubeMapHigh);
                return 20 + loc;

            }
            if (hash == (int)DummyTextures.DummyRamp)
            {
                GL.ActiveTexture(TextureUnit.Texture20 + loc);
                GL.BindTexture(TextureTarget.Texture2D, RenderTools.defaultRamp);
                return 20 + loc;
            }
            GL.ActiveTexture(TextureUnit.Texture3 + loc);
            GL.BindTexture(TextureTarget.Texture2D, RenderTools.defaultTex);

            int texid;
            bool success;
            foreach (NUT nut in Runtime.TextureContainers)
            {
                success = nut.draw.TryGetValue(hash, out texid);

                if (success)
                {
                    GL.BindTexture(TextureTarget.Texture2D, texid);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrapmode[tex.WrapMode1]);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrapmode[tex.WrapMode2]);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minfilter[tex.minFilter]);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magfilter[tex.magFilter]);
                    GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, 0.0f);
                    if(tex.mipDetail == 0x4 || tex.mipDetail == 0x6)
                        GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, 4.0f);
                    break;
                }
            }

            return 3 + loc;
        }

        #endregion

        #region MTA
        public void clearMTA()
        {
            foreach (Mesh me in mesh)
            {
                foreach (Polygon p in me.Nodes)
                {
                    foreach (Material ma in p.materials)
                    {
                        ma.anims.Clear();
                    }
                }
            }
        }

        public void applyMTA(MTA m, int frame)
        {
            foreach (MatEntry mat in m.matEntries)
            {
                foreach (Mesh me in mesh)
                {
                    foreach(Polygon p in me.Nodes)
                    {
                        foreach (Material ma in p.materials)
                        {
                            float[] matHashFloat;
                            ma.entries.TryGetValue("NU_materialHash", out matHashFloat);
                            if (matHashFloat != null) {

                                byte[] bytes = new byte[4];
                                Buffer.BlockCopy(matHashFloat, 0, bytes, 0, 4);
                                int matHash = BitConverter.ToInt32(bytes, 0);

                                int frm = (int)((frame * 60 / m.frameRate) % (m.numFrames));

                                if (matHash == mat.matHash || matHash == mat.matHash2)
                                {
                                    if (mat.hasPat)
                                    {
                                        ma.displayTexId = mat.pat0.getTexId(frm);
                                    }

                                    foreach(MatData md in mat.properties)
                                    {
                                        if (md.frames.Count > 0)
                                        {
                                            if (ma.anims.ContainsKey(md.name))
                                                ma.anims[md.name] = md.frames[frm].values;
                                            else
                                                if(md.frames.Count > frm)
                                                    ma.anims.Add(md.name, md.frames[frm].values);
                                        }
                                            
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (VisEntry e in m.visEntries)
            {
                int state = e.getState(frame);
                foreach (Mesh me in mesh)
                {
                    if (me.Text.Equals(e.name))
                    {
                        if (state == 0)
                        {
                            me.Checked = false;
                        }
                        else
                        {
                            me.Checked = true;
                        }
                        break;
                    }
                }
            }

        }
        #endregion

        #region Reading
        //------------------------------------------------------------------------------------------------------------------------
        /*
         * Reads the contents of the nud file into this class
         */
        //------------------------------------------------------------------------------------------------------------------------
        // HELPERS FOR READING
        private struct _s_Object
        {
            public int id;
            //public int polynamestart;
            public int singlebind;
            public int polyamt;
            public int positionb;
            public string name;
        }

        public struct _s_Poly
        {
            public int polyStart;
            public int vertStart;
            public int verAddStart;
            public int vertamt;
            public int vertSize;
            public int UVSize;
            public int polyamt;
            public int polsize;
            public int polflag;
            public int texprop1;
            public int texprop2;
            public int texprop3;
            public int texprop4;
        }

        public override void Read(string filename)
        {
            FileData d = new FileData(filename);
            d.Endian = Endianness.Big;
            d.seek(0);

            // read header
            string magic = d.readString(0, 4);

            if (magic.Equals("NDWD"))
                d.Endian = Endianness.Little;

            Endian = d.Endian;

            d.seek(0xA);
            int polysets = d.readShort();
            boneCount = d.readShort();
            d.skip(2);  // somethingsets
            int polyClumpStart = d.readInt() + 0x30;
            int polyClumpSize = d.readInt();
            int vertClumpStart = polyClumpStart + polyClumpSize;
            int vertClumpSize = d.readInt();
            int vertaddClumpStart = vertClumpStart + vertClumpSize;
            int vertaddClumpSize = d.readInt();
            int nameStart = vertaddClumpStart + vertaddClumpSize;
            param[0] = d.readFloat();
            param[1] = d.readFloat();
            param[2] = d.readFloat();
            param[3] = d.readFloat();

            // object descriptors

            _s_Object[] obj = new _s_Object[polysets];
            List<float[]> unknown = new List<float[]>();
            int[] boneflags = new int[polysets];
            for (int i = 0; i < polysets; i++)
            {
                float[] un = new float[8];
                un[0] = d.readFloat();
                un[1] = d.readFloat();
                un[2] = d.readFloat();
                un[3] = d.readFloat();
                un[4] = d.readFloat();
                un[5] = d.readFloat();
                un[6] = d.readFloat();
                un[7] = d.readFloat();
                unknown.Add(un);
                int temp = d.pos() + 4;
                d.seek(nameStart + d.readInt());
                obj[i].name = (d.readString());
                // read name string
                d.seek(temp);
                boneflags[i] = d.readInt();
                obj[i].singlebind = d.readShort();
                obj[i].polyamt = d.readShort();
                obj[i].positionb = d.readInt();
            }

            // reading polygon data
            int mi = 0;
            foreach (var o in obj)
            {
                Mesh m = new Mesh();
                m.Text = o.name;
                mesh.Add(m);
                m.boneflag = boneflags[mi];
                m.singlebind = (short)o.singlebind;
                m.boundingBox = unknown[mi++];

                for (int i = 0; i < o.polyamt; i++)
                {
                    _s_Poly p = new _s_Poly();

                    p.polyStart = d.readInt() + polyClumpStart;
                    p.vertStart = d.readInt() + vertClumpStart;
                    p.verAddStart = d.readInt() + vertaddClumpStart;
                    p.vertamt = d.readShort();
                    p.vertSize = d.readByte();
                    p.UVSize = d.readByte();
                    p.texprop1 = d.readInt();
                    p.texprop2 = d.readInt();
                    p.texprop3 = d.readInt();
                    p.texprop4 = d.readInt();
                    p.polyamt = d.readShort();
                    p.polsize = d.readByte();
                    p.polflag = d.readByte();
                    d.skip(0xC);

                    int temp = d.pos();

                    // read vertex
                    Polygon pol = readVertex(d, p, o);
                    m.Nodes.Add(pol);

                    pol.materials = readMaterial(d, p, nameStart);

                    d.seek(temp);
                }
            }
        }

        //VERTEX TYPES----------------------------------------------------------------------------------------

        public static List<Material> readMaterial(FileData d, _s_Poly p, int nameOffset)
        {
            int propoff = p.texprop1;
            List<Material> mats = new List<Material>();

            while (propoff != 0)
            {
                d.seek(propoff);

                Material m = new Material();
                mats.Add(m);

                m.flags = (uint)d.readInt();
                d.skip(4);

                
                m.srcFactor = d.readShort();
                int propCount = d.readShort();
                m.dstFactor = d.readShort();
                m.AlphaTest = d.readByte();
                m.AlphaFunc = d.readByte();

                d.skip(1); // unknown
                m.RefAlpha = d.readByte();
                m.cullMode = d.readShort();
                d.skip(4); // padding
                m.unkownWater = d.readInt();
                m.zBufferOffset = d.readInt();

                for (int i = 0; i < propCount; i++)
                {
                    Mat_Texture tex = new Mat_Texture();
                    tex.hash = d.readInt();
                    d.skip(6); // padding?
                    tex.MapMode = d.readShort();
                    tex.WrapMode1 = d.readByte();
                    tex.WrapMode2 = d.readByte();
                    tex.minFilter = d.readByte();
                    tex.magFilter = d.readByte();
                    tex.mipDetail = d.readByte();
                    tex.unknown = d.readByte();
                    d.skip(6);
                    m.textures.Add(tex);
                }

                int head = 0x20;

                if(d.Endian != Endianness.Little)
                while (head != 0)
                {
                    head = d.readInt();
                    int nameStart = d.readInt();

                    string name = d.readString(nameOffset + nameStart, -1);

                    int pos = d.pos();
                    int c = d.readInt();
                    Debug.WriteLine(c);
                    d.skip(4);
                    float[] values = new float[c];
                    for (int i = 0; i < c; i++)
                    {
                        values[i] = d.readFloat();
                    }

                    // material properties should always have 4 values
                    if (values.Length < 4)
                    {
                        float[] newValues = { 0, 0, 0, 0 };
                        for (int i = 0; i < values.Length; i++)
                        {
                            // fill in existing values and use 0 for remaining values
                            newValues[i] = values[i];
                        }

                        m.entries.Add(name, newValues);
                    }
                    else
                        m.entries.Add(name, values);

                    d.seek(pos);

                    if (head == 0)
                        d.skip(0x20 - 8);
                    else
                        d.skip(head - 8);
                }

                if (propoff == p.texprop1)
                    propoff = p.texprop2;
                else
                    if (propoff == p.texprop2)
                        propoff = p.texprop3;
                    else
                        if (propoff == p.texprop3)
                            propoff = p.texprop4;
            }

            return mats;
        }

        private static Polygon readVertex(FileData d, _s_Poly p, _s_Object o)
        {
            Polygon m = new Polygon();
            m.vertSize = p.vertSize;
            m.UVSize = p.UVSize;
            m.polflag = p.polflag;
            m.strip = p.polsize;

            readVertex(d, p, o, m);

            // faces
            d.seek(p.polyStart);

            for (int x = 0; x < p.polyamt; x++)
            {
                m.faces.Add(d.readShort());
            }

            return m;
        }

        //VERTEX TYPES----------------------------------------------------------------------------------------
        private static void readUV(FileData d, _s_Poly p, _s_Object o, Polygon m, Vertex[] v)
        {
            int uvCount = (p.UVSize >> 4);
            int uvType = (p.UVSize) & 0xF;

            for (int i = 0; i < p.vertamt; i++)
            {
                v[i] = new Vertex();
                if (uvType == 0x0)
                {
                    for (int j = 0; j < uvCount; j++)
                        v[i].tx.Add(new Vector2(d.readHalfFloat(), d.readHalfFloat()));
                }
                else
                    if (uvType == 0x2)
                    {
                        v[i].col = new Vector4(d.readByte(), d.readByte(), d.readByte(), d.readByte());
                        for (int j = 0; j < uvCount; j++)
                            v[i].tx.Add(new Vector2(d.readHalfFloat(), d.readHalfFloat()));
                }
                else
                    if (uvType == 0x4)
                {
                    v[i].col = new Vector4(d.readHalfFloat() * 0xFF, d.readHalfFloat() * 0xFF, d.readHalfFloat() * 0xFF, d.readHalfFloat() * 0xFF);
                    for (int j = 0; j < uvCount; j++)
                        v[i].tx.Add(new Vector2(d.readHalfFloat(), d.readHalfFloat()));
                }
                else
                        throw new NotImplementedException("UV type not supported " + uvType);
            }
        }

        private static void readVertex(FileData d, _s_Poly p, _s_Object o, Polygon m)
        {
            int weight = p.vertSize >> 4;
            int nrm = p.vertSize & 0xF;

            Vertex[] v = new Vertex[p.vertamt];

            d.seek(p.vertStart);

            if (weight > 0)
            {
                readUV(d, p, o, m, v);
                d.seek(p.verAddStart);
            }
            else
            {
                for (int i = 0; i < p.vertamt; i++)
                {
                    v[i] = new Vertex();
                }
            }

            for (int i = 0; i < p.vertamt; i++)
            {
                if (nrm != 8)
                {
                    v[i].pos.X = d.readFloat();
                    v[i].pos.Y = d.readFloat();
                    v[i].pos.Z = d.readFloat();
                }

                if (nrm == 1)
                {
                    v[i].nrm.X = d.readFloat();
                    v[i].nrm.Y = d.readFloat();
                    v[i].nrm.Z = d.readFloat();
                    d.skip(4); // n1?
                    d.skip(4); // r1?
                } else if (nrm == 2)
                {
                    v[i].nrm.X = d.readFloat();
                    v[i].nrm.Y = d.readFloat();
                    v[i].nrm.Z = d.readFloat();
                    d.skip(4); // n1?
                    d.skip(12); // r1?
                    d.skip(12); // r1?
                    d.skip(12); // r1?
                } else if (nrm == 3)
                {
                    d.skip(4); 
                    v[i].nrm.X = d.readFloat();
                    v[i].nrm.Y = d.readFloat();
                    v[i].nrm.Z = d.readFloat();
                    d.skip(4); 
                    v[i].bitan.X = d.readFloat();
                    v[i].bitan.Y = d.readFloat();
                    v[i].bitan.Z = d.readFloat();
                    v[i].bitan.W = d.readFloat();
                    v[i].tan.X = d.readFloat();
                    v[i].tan.Y = d.readFloat();
                    v[i].tan.Z = d.readFloat();
                    v[i].tan.W = d.readFloat();
                }
                else if (nrm == 6)
                {
                    v[i].nrm.X = d.readHalfFloat();
                    v[i].nrm.Y = d.readHalfFloat();
                    v[i].nrm.Z = d.readHalfFloat();
                    d.skip(2); // n1?
                } else if (nrm == 7)
                {
                    v[i].nrm.X = d.readHalfFloat();
                    v[i].nrm.Y = d.readHalfFloat();
                    v[i].nrm.Z = d.readHalfFloat();
                    d.skip(2); // n1?
                    v[i].bitan.X = d.readHalfFloat();
                    v[i].bitan.Y = d.readHalfFloat();
                    v[i].bitan.Z = d.readHalfFloat();
                    v[i].bitan.W = d.readHalfFloat();
                    v[i].tan.X = d.readHalfFloat();
                    v[i].tan.Y = d.readHalfFloat();
                    v[i].tan.Z = d.readHalfFloat();
                    v[i].tan.W = d.readHalfFloat();
                } else
                    d.skip(4);

                if (weight == 0)
                {
                    if (p.UVSize >= 18)
                    {
                        v[i].col.X = (int)d.readByte();
                        v[i].col.Y = (int)d.readByte();
                        v[i].col.Z = (int)d.readByte();
                        v[i].col.W = (int)d.readByte();
                        //v.a = (int) (d.readByte());
                    }

                    for (int j = 0; j < (p.UVSize >> 4); j++)
                        v[i].tx.Add(new Vector2(d.readHalfFloat(), d.readHalfFloat()));

                    // UV layers
                    //d.skip(4 * ((p.UVSize >> 4) - 1));
                }

                if (weight == 1)
                {
                    v[i].node.Add(d.readInt());
                    v[i].node.Add(d.readInt());
                    v[i].node.Add(d.readInt());
                    v[i].node.Add(d.readInt());
                    v[i].weight.Add(d.readFloat());
                    v[i].weight.Add(d.readFloat());
                    v[i].weight.Add(d.readFloat());
                    v[i].weight.Add(d.readFloat());
                }
                else if (weight == 2)
                {
                    v[i].node.Add(d.readShort());
                    v[i].node.Add(d.readShort());
                    v[i].node.Add(d.readShort());
                    v[i].node.Add(d.readShort());
                    v[i].weight.Add(d.readHalfFloat());
                    v[i].weight.Add(d.readHalfFloat());
                    v[i].weight.Add(d.readHalfFloat());
                    v[i].weight.Add(d.readHalfFloat());
                }
                else if (weight == 4)
                {
                    v[i].node.Add(d.readByte());
                    v[i].node.Add(d.readByte());
                    v[i].node.Add(d.readByte());
                    v[i].node.Add(d.readByte());
                    v[i].weight.Add((float)d.readByte() / 255f);
                    v[i].weight.Add((float)d.readByte() / 255f);
                    v[i].weight.Add((float)d.readByte() / 255f);
                    v[i].weight.Add((float)d.readByte() / 255f);
                }
                else if (weight == 0)
                {
                    v[i].node.Add((short)o.singlebind);
                    v[i].weight.Add(1);
                }
            }

            foreach (Vertex vi in v)
                m.vertices.Add(vi);
        }
        #endregion

        #region Building
        public override byte[] Rebuild()
        {
            FileOutput d = new FileOutput(); // data
            d.Endian = Endianness.Big;

            // mesh optimize

            d.writeString("NDP3");
            d.writeInt(0); //FileSize
            d.writeShort(0x200); //  version num
            d.writeShort(mesh.Count); // polysets

            foreach (ModelContainer con in Runtime.ModelContainers)
            {
                if (con.nud == this && con.vbn!=null)
                    boneCount = con.vbn.bones.Count;   
            }

            d.writeShort(boneCount == 0 ? 0 : 2); // type
            d.writeShort(boneCount == 0 ? boneCount : boneCount - 1); // Number of bones

            d.writeInt(0); // polyClump start
            d.writeInt(0); // polyClump size
            d.writeInt(0); // vertexClumpsize
            d.writeInt(0); // vertexaddcump size
            
            d.writeFloat(param[0]);
            d.writeFloat(param[1]);
            d.writeFloat(param[2]);
            d.writeFloat(param[3]);

            // other sections....
            FileOutput obj = new FileOutput(); // data
            obj.Endian = Endianness.Big;
            FileOutput tex = new FileOutput(); // data
            tex.Endian = Endianness.Big;

            FileOutput poly = new FileOutput(); // data
            poly.Endian = Endianness.Big;
            FileOutput vert = new FileOutput(); // data
            vert.Endian = Endianness.Big;
            FileOutput vertadd = new FileOutput(); // data
            vertadd.Endian = Endianness.Big;

            FileOutput str = new FileOutput(); // data
            str.Endian = Endianness.Big;


            // obj descriptor

            FileOutput tempstring = new FileOutput(); // data
            for (int i = 0; i < mesh.Count; i++)
            {
                str.writeString(mesh[i].Text);
                str.writeByte(0);
                str.align(16);
            }

            int polyCount = 0; // counting number of poly
            foreach (Mesh m in mesh)
                polyCount += m.Nodes.Count;

            for (int i = 0; i < mesh.Count; i++)
            {
                foreach (float f in mesh[i].boundingBox)
                    d.writeFloat(f);

                d.writeInt(tempstring.size());

                tempstring.writeString(mesh[i].Text);
                tempstring.writeByte(0);
                tempstring.align(16);

                d.writeInt(mesh[i].boneflag); // ID
                d.writeShort(mesh[i].singlebind); // Single Bind 
                d.writeShort(mesh[i].Nodes.Count); // poly count
                d.writeInt(obj.size() + 0x30 + mesh.Count * 0x30); // position start for obj

                // write obj info here...
                for (int k = 0; k < mesh[i].Nodes.Count; k++)
                {
                    obj.writeInt(poly.size());
                    obj.writeInt(vert.size());
                    obj.writeInt(((NUD.Polygon)mesh[i].Nodes[k]).vertSize >> 4 > 0 ? vertadd.size() : 0);
                    obj.writeShort(((NUD.Polygon)mesh[i].Nodes[k]).vertices.Count);
                    obj.writeByte(((NUD.Polygon)mesh[i].Nodes[k]).vertSize); // type of vert

                    int maxUV = ((NUD.Polygon)mesh[i].Nodes[k]).vertices[0].tx.Count; // TODO: multi uv stuff  mesh[i].polygons[k].maxUV() + 

                    obj.writeByte(((NUD.Polygon)mesh[i].Nodes[k]).UVSize); 

                    // MATERIAL SECTION 

                    FileOutput te = new FileOutput();
                    te.Endian = Endianness.Big;

                    int[] texoff = writeMaterial(tex, ((NUD.Polygon)mesh[i].Nodes[k]).materials, str);
                    //tex.writeOutput(te);

                    //obj.writeInt(tex.size() + 0x30 + mesh.Count * 0x30 + polyCount * 0x30); // Tex properties... This is tex offset
                    obj.writeInt(texoff[0] + 0x30 + mesh.Count * 0x30 + polyCount * 0x30);
                    obj.writeInt(texoff[1] > 0 ? texoff[1] + 0x30 + mesh.Count * 0x30 + polyCount * 0x30 : 0);
                    obj.writeInt(texoff[2] > 0 ? texoff[2] + 0x30 + mesh.Count * 0x30 + polyCount * 0x30 : 0);
                    obj.writeInt(texoff[3] > 0 ? texoff[3] + 0x30 + mesh.Count * 0x30 + polyCount * 0x30 : 0);

                    obj.writeShort(((NUD.Polygon)mesh[i].Nodes[k]).faces.Count); // polyamt
                    obj.writeByte(((NUD.Polygon)mesh[i].Nodes[k]).strip); // polysize 0x04 is strips and 0x40 is easy
                    // :D
                    obj.writeByte(((NUD.Polygon)mesh[i].Nodes[k]).polflag); // polyflag

                    obj.writeInt(0); // idk, nothing padding??
                    obj.writeInt(0);
                    obj.writeInt(0);

                    // Write the poly...
                    foreach (int face in ((NUD.Polygon)mesh[i].Nodes[k]).faces)
                        poly.writeShort(face);

                    // Write the vertex....

                    writeVertex(vert, vertadd, ((NUD.Polygon)mesh[i].Nodes[k]));
                    vertadd.align(4, 0x0);
                }
            }

            //
            d.writeOutput(obj);
            d.writeOutput(tex);
            d.align(16);

            d.writeIntAt(d.size() - 0x30, 0x10);
            d.writeIntAt(poly.size(), 0x14);
            d.writeIntAt(vert.size(), 0x18);
            d.writeIntAt(vertadd.size(), 0x1c);

            d.writeOutput(poly);

            int s = d.size();
            d.align(16);
            s = d.size() - s;
            d.writeIntAt(poly.size() + s, 0x14);

            d.writeOutput(vert);

            s = d.size();
            d.align(16);
            s = d.size() - s;
            d.writeIntAt(vert.size() + s, 0x18);

            d.writeOutput(vertadd);

            s = d.size();
            d.align(16);
            s = d.size() - s;
            d.writeIntAt(vertadd.size() + s, 0x1c);

            d.writeOutput(str);

            d.writeIntAt(d.size(), 0x4);

            return d.getBytes();
        }

        private static void writeUV(FileOutput d, Polygon m)
        {
            int uvCount = (m.UVSize >> 4);
            int uvType = (m.UVSize) & 0xF;

            for (int i = 0; i < m.vertices.Count; i++)
            {

                if (uvType == 0x0)
                {
                    for (int j = 0; j < uvCount; j++)
                    {
                        d.writeHalfFloat(m.vertices[i].tx[j].X);
                        d.writeHalfFloat(m.vertices[i].tx[j].Y);
                    }
                }else
                if (uvType == 0x2)
                {
                    d.writeByte((int)m.vertices[i].col.X);
                    d.writeByte((int)m.vertices[i].col.Y);
                    d.writeByte((int)m.vertices[i].col.Z);
                    d.writeByte((int)m.vertices[i].col.W);
                    for (int j = 0; j < uvCount; j++)
                    {
                        d.writeHalfFloat(m.vertices[i].tx[j].X);
                        d.writeHalfFloat(m.vertices[i].tx[j].Y);
                    }
                }else
                if (uvType == 0x4)
                {
                    d.writeHalfFloat(m.vertices[i].col.X / 0xFF);
                    d.writeHalfFloat(m.vertices[i].col.Y / 0xFF);
                    d.writeHalfFloat(m.vertices[i].col.Z / 0xFF);
                    d.writeHalfFloat(m.vertices[i].col.W / 0xFF);
                    for (int j = 0; j < uvCount; j++)
                    {
                        d.writeHalfFloat(m.vertices[i].tx[j].X);
                        d.writeHalfFloat(m.vertices[i].tx[j].Y);
                    }
                }
                else
                    throw new NotImplementedException("Unsupported UV format");
            }
        }

        private static void writeVertex(FileOutput d, FileOutput add, Polygon m)
        {
            int weight = m.vertSize >> 4;
            int nrm = m.vertSize & 0xF;
            
            if (weight > 0)
            {
                writeUV(d, m);
                d = add;
            }

            for (int i = 0; i < m.vertices.Count; i++)
            {
                
                Vertex v = m.vertices[i];
                if (nrm < 8)
                {
                    d.writeFloat(v.pos.X);
                    d.writeFloat(v.pos.Y);
                    d.writeFloat(v.pos.Z);
                }
                
                if(nrm == 0)
                {
                    d.writeInt(0);
                }
                else if (nrm == 1)
                {
                    d.writeFloat(v.nrm.X);
                    d.writeFloat(v.nrm.Y);
                    d.writeFloat(v.nrm.Z);
                    d.writeFloat(1);
                    d.writeFloat(1);
                }else if (nrm == 2)
                {
                    d.writeFloat(v.nrm.X);
                    d.writeFloat(v.nrm.Y);
                    d.writeFloat(v.nrm.Z);
                    d.writeFloat(1);
                    d.writeFloat(v.bitan.X); d.writeFloat(v.bitan.Y); d.writeFloat(v.bitan.Z);
                    d.writeFloat(1);
                    d.writeFloat(v.tan.X); d.writeFloat(v.tan.Y); d.writeFloat(v.tan.Z);
                    d.writeFloat(1);
                    d.writeFloat(1);
                }else if (nrm == 3)
                {
                    d.writeFloat(1);
                    d.writeFloat(v.nrm.X);
                    d.writeFloat(v.nrm.Y);
                    d.writeFloat(v.nrm.Z);
                    d.writeFloat(1);
                    // bn and tan floats
                    d.writeFloat(m.vertices[i].bitan.X);
                    d.writeFloat(m.vertices[i].bitan.Y);
                    d.writeFloat(m.vertices[i].bitan.Z);
                    d.writeFloat(m.vertices[i].bitan.W);
                    d.writeFloat(m.vertices[i].tan.X);
                    d.writeFloat(m.vertices[i].tan.Y);
                    d.writeFloat(m.vertices[i].tan.Z);
                    d.writeFloat(m.vertices[i].tan.W);
                }
                else
                if (nrm == 6)
                {
                    d.writeHalfFloat(v.nrm.X);
                    d.writeHalfFloat(v.nrm.Y);
                    d.writeHalfFloat(v.nrm.Z);
                    d.writeHalfFloat(1);
                }
                else
                if (nrm == 7)
                {
                    d.writeHalfFloat(v.nrm.X);
                    d.writeHalfFloat(v.nrm.Y);
                    d.writeHalfFloat(v.nrm.Z);
                    d.writeHalfFloat(1);
                    // bn and tan half floats
                    d.writeHalfFloat(m.vertices[i].bitan.X);
                    d.writeHalfFloat(m.vertices[i].bitan.Y);
                    d.writeHalfFloat(m.vertices[i].bitan.Z);
                    d.writeHalfFloat(m.vertices[i].bitan.W);
                    d.writeHalfFloat(m.vertices[i].tan.X);
                    d.writeHalfFloat(m.vertices[i].tan.Y);
                    d.writeHalfFloat(m.vertices[i].tan.Z);
                    d.writeHalfFloat(m.vertices[i].tan.W);
                }

                if (weight == 0)
                {
                    if (m.UVSize >= 18)
                    {
                        d.writeByte((int)m.vertices[i].col.X);
                        d.writeByte((int)m.vertices[i].col.Y);
                        d.writeByte((int)m.vertices[i].col.Z);
                        d.writeByte((int)m.vertices[i].col.W);
                    }

                    for (int j = 0; j < m.vertices[i].tx.Count; j++)
                    {
                        d.writeHalfFloat(m.vertices[i].tx[j].X);
                        d.writeHalfFloat(m.vertices[i].tx[j].Y);
                    }

                    // UV layers
                    //d.skip(4 * ((m.UVSize >> 4) - 1));
                }

                if (weight == 1)
                {
                    d.writeInt(v.node.Count > 0 ? v.node[0] : 0);
                    d.writeInt(v.node.Count > 1 ? v.node[1] : 0);
                    d.writeInt(v.node.Count > 2 ? v.node[2] : 0);
                    d.writeInt(v.node.Count > 3 ? v.node[3] : 0);
                    d.writeFloat(v.weight.Count > 0 ? v.weight[0] : 0);
                    d.writeFloat(v.weight.Count > 1 ? v.weight[1] : 0);
                    d.writeFloat(v.weight.Count > 2 ? v.weight[2] : 0);
                    d.writeFloat(v.weight.Count > 3 ? v.weight[3] : 0);
                }
                if (weight == 2)
                {
                    d.writeShort(v.node.Count > 0 ? v.node[0] : 0);
                    d.writeShort(v.node.Count > 1 ? v.node[1] : 0);
                    d.writeShort(v.node.Count > 2 ? v.node[2] : 0);
                    d.writeShort(v.node.Count > 3 ? v.node[3] : 0);
                    d.writeHalfFloat(v.weight.Count > 0 ? v.weight[0] : 0);
                    d.writeHalfFloat(v.weight.Count > 1 ? v.weight[1] : 0);
                    d.writeHalfFloat(v.weight.Count > 2 ? v.weight[2] : 0);
                    d.writeHalfFloat(v.weight.Count > 3 ? v.weight[3] : 0);
                }
                if (weight == 4)
                {
                    d.writeByte(v.node.Count > 0 ? v.node[0] : 0);
                    d.writeByte(v.node.Count > 1 ? v.node[1] : 0);
                    d.writeByte(v.node.Count > 2 ? v.node[2] : 0);
                    d.writeByte(v.node.Count > 3 ? v.node[3] : 0);
                    d.writeByte((int)(v.weight.Count > 0 ? Math.Round(v.weight[0] * 0xFF) : 0));
                    d.writeByte((int)(v.weight.Count > 1 ? Math.Round(v.weight[1] * 0xFF) : 0));
                    d.writeByte((int)(v.weight.Count > 2 ? Math.Round(v.weight[2] * 0xFF) : 0));
                    d.writeByte((int)(v.weight.Count > 3 ? Math.Round(v.weight[3] * 0xFF) : 0));
                }
            }
        }

        public static int[] writeMaterial(FileOutput d, List<Material> materials, FileOutput str)
        {
            int[] offs = new int[4];
            int c = 0;
            foreach (Material mat in materials)
            {
                offs[c++] = d.size();
                d.writeInt((int)mat.flags);
                d.writeInt(0); // padding
                d.writeShort(mat.srcFactor);
                d.writeShort(mat.textures.Count);
                d.writeShort(mat.dstFactor);
                d.writeByte(mat.AlphaTest);
                d.writeByte(mat.AlphaFunc);
                d.writeByte(0); // unknown padding?
                d.writeByte(mat.RefAlpha);
                d.writeShort(mat.cullMode);
                d.writeInt(0); // padding
                d.writeInt(mat.unkownWater); 
                d.writeInt(mat.zBufferOffset); 

                foreach (Mat_Texture tex in mat.textures)
                {
                    d.writeInt(tex.hash);
                    d.writeInt(0);
                    d.writeShort(0);
                    d.writeShort(tex.MapMode);
                    d.writeByte(tex.WrapMode1);
                    d.writeByte(tex.WrapMode2);
                    d.writeByte(tex.minFilter);
                    d.writeByte(tex.magFilter);
                    d.writeByte(tex.mipDetail);
                    d.writeByte(tex.unknown);
                    d.writeInt(0); // padding
                    d.writeShort(0);
                }

                for (int i = 0; i < mat.entries.Count; i++)
                {
                    float[] data;
                    mat.entries.TryGetValue(mat.entries.ElementAt(i).Key, out data);
                    d.writeInt(i == mat.entries.Count - 1 ? 0 : 16 + 4 * data.Length);
                    d.writeInt(str.size());

                    str.writeString(mat.entries.ElementAt(i).Key);
                    str.writeByte(0);
                    str.align(16);

                    d.writeInt(data.Length);
                    d.writeInt(0);
                    foreach (float f in data)
                        d.writeFloat(f);
                }
            }
            return offs;
        }

        #endregion
        
        #region Functions
        public void MergePoly()
        {
            Dictionary<string, Mesh> nmesh = new Dictionary<string, Mesh>();
            foreach(Mesh m in mesh)
            {
                if (nmesh.ContainsKey(m.Text))
                {
                    // merge poly
                    foreach(Polygon p in m.Nodes)
                        nmesh[m.Text].Nodes.Add(p);
                } else
                {
                    nmesh.Add(m.Text, m);
                }
            }
            // consolidate
            mesh.Clear();
            foreach (string n in nmesh.Keys)
            {
                mesh.Add(nmesh[n]);
            }
            PreRender();
        }
        #endregion

        #region ClassStructure

        public struct Vector4i
        {
            int x, y, z, w;

            public Vector4i(int x, int y, int z, int w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }

            public static int Size = 4 * sizeof(int);
        }

        public struct dVertex
        {
            public Vector3 pos;
            public Vector3 nrm;
            public Vector3 tan;
            public Vector3 bit;
            public Vector2 tx0;
            public Vector4 col;
            public Vector4 node;
            public Vector4 weight;

            public static int Size = 4 * (3 + 3 + 3 + 3 + 2 + 4 + 4 + 4);
        }

        public class Vertex
        {
            public Vector3 pos = new Vector3(0, 0, 0), nrm = new Vector3(0, 0, 0);
            public Vector4 bitan = new Vector4(0, 0, 0, 1), tan = new Vector4(0, 0, 0, 1);
            public Vector4 col = new Vector4(127, 127, 127, 127);
            public List<Vector2> tx = new List<Vector2>();
            public List<int> node = new List<int>();
            public List<float> weight = new List<float>();

            public Vertex()
            {
            }

            public Vertex(float x, float y, float z)
            {
                pos = new Vector3(x, y, z);
            }

            public bool Equals(Vertex p)
            {
                return pos.Equals(p.pos) && new HashSet<Vector2>(tx).SetEquals(p.tx) && col.Equals(p.col)
                    && new HashSet<int>(node).SetEquals(p.node) && new HashSet<float>(weight).SetEquals(p.weight);
            }

            public override string ToString()
            {
                return pos.ToString();
            }
        }


        public class Mat_Texture
        {
            public int hash;
            public int MapMode = 0;
            public int WrapMode1 = 0;
            public int WrapMode2 = 0;
            public int minFilter = 0;
            public int magFilter = 0;
            public int mipDetail = 0;
            public int unknown = 0;

            public Mat_Texture Clone()
            {
                Mat_Texture t = new Mat_Texture();
                t.hash = hash;
                t.MapMode = MapMode;
                t.WrapMode1 = WrapMode1;
                t.WrapMode2 = WrapMode2;
                t.minFilter = minFilter;
                t.magFilter = magFilter;
                t.mipDetail = mipDetail;
                t.unknown = unknown;
                return t;
            }
        }

        public enum SrcFactors 
        {
            Nothing = 0x0,
            SourceAlpha = 0x1,
            One = 0x2,
            InverseSourceAlpha = 0x3,
            SourceColor = 0x4,
            Zero = 0xA
        }

        public class Material
        {
            public Dictionary<string, float[]> entries = new Dictionary<string, float[]>();
            public Dictionary<string, float[]> anims = new Dictionary<string, float[]>();
            public List<Mat_Texture> textures = new List<Mat_Texture>();

            public uint flags { get
                {
                    return RebuildFlag4thByte();
                } set
                {
                    InterpretFlags(value);
                } }
            private uint flag;
            public int blendMode = 0;
            public int dstFactor = 0;
            public int srcFactor = 0;
            public int AlphaTest = 0;
            public int AlphaFunc = 0;
            public int RefAlpha = 0;
            public int cullMode = 0;
            public int displayTexId = -1;

            public int unknown1 = 0;
            public int unkownWater = 0;
            public int zBufferOffset = 0;

            //flags
            public bool glow = false;
            public bool hasShadow = false;
            public bool useVertexColor = false;
            public bool useColorGainOffset = false;
            public bool useDiffuseBlend = false;

            public bool diffuse = false;
            public bool normalmap = false;
            public bool diffuse2 = false;
            public bool diffuse3 = false;
            public bool aomap = false;
            public bool stagemap = false;
            public bool cubemap = false;
            public bool ramp = false;
            public bool spheremap = false;
            public bool dummyramp = false;

            // texture IDs for preserving existing textures
            public int diffuse1ID = 0;
            public int diffuse2ID = 0;
            public int diffuse3ID = 0;
            public int normalID = 0;
            public int rampID = (int)DummyTextures.DummyRamp;
            public int dummyRampID = (int)DummyTextures.DummyRamp;
            public int sphereMapID = 0;
            public int aoMapID = 0;
            public int stageMapID = (int)DummyTextures.StageMapHigh;
            public int cubeMapID = 0;

            public Material Clone()
            {
                Material m = new Material();

                foreach (KeyValuePair<string, float[]> e in entries)
                    m.entries.Add(e.Key, e.Value);

                m.flags = flags;
                m.blendMode = blendMode;
                m.dstFactor = dstFactor;
                m.srcFactor = srcFactor;
                m.AlphaTest = AlphaTest;
                m.AlphaFunc = AlphaFunc;
                m.RefAlpha = RefAlpha;
                m.cullMode = cullMode;
                m.displayTexId = displayTexId;

                m.unknown1 = 0;
                m.unkownWater = 0;
                m.zBufferOffset = 0;

                foreach(Mat_Texture t in textures)
                {
                    m.textures.Add(t.Clone());
                }

                return m;
            }

            public Material()
            {
            }

            public uint RebuildFlag4thByte()
            {
                int t = 0;
                if (diffuse) t |= (int)TextureFlags.DiffuseMap;
                if (normalmap) t |= (int)TextureFlags.NormalMap;
                if (cubemap || ramp) t |= (int)TextureFlags.RampCubeMap;
                if (stagemap || aomap) t |= (int)TextureFlags.StageAOMap;
                if (spheremap) t |= (int)TextureFlags.SphereMap;
                if (glow) t |= (int) TextureFlags.Glow;
                if (hasShadow) t |= (int) TextureFlags.Shadow;
                if (dummyramp) t |= (int) TextureFlags.DummyRamp; 
                if (useColorGainOffset) t |= 0x0C000061;
                flag = (uint)(((int)flag & 0xFFFFFF00) | t);

                return flag;
            }

            public void InterpretFlags(uint flags)
            {
                // also need to clean this up
                // set depending on flags
                this.flag = flags;
                int flag = ((int)flags) & 0xFF;
                glow = (flag & (int) TextureFlags.Glow) > 0;
                hasShadow = (flag & (int) TextureFlags.Shadow) > 0;
                dummyramp = (flag & (int) TextureFlags.DummyRamp) > 0;
                spheremap = (flag & (int) TextureFlags.SphereMap) > 0;
                TestTextures();

                // check lighting channel and 4th byte of flags
                flag = ((int)flags);

                bool colorGainLightingChannel = (flag & 0x0C000000) == 0x0C000000;
                bool colorGain2ndByte = ((flag & 0x00FF0000) == 0x00610000 || (flag & 0x00FF0000) == 0x00420000 || (flag & 0x00FF0000) == 0x00440000);
                bool colorGain4thByte = (flag & 0x000000FF) == 0x00000061;

                useColorGainOffset = colorGainLightingChannel && colorGain2ndByte && colorGain4thByte;

                useDiffuseBlend = (flag & 0xD0090000) == 0xD0090000 || (flag & 0x90005000) == 0x90005000;

                // characters, stages with certain flags
                useVertexColor = (flag & 0x0F000000) == 0x02000000 || (flag & 0x0F000000) == 0x04000000 
                    || (flag & 0x0F000000) == 0x06000000 || (flag & 0xF0000000) == 0x90000000; 

            }

            public void TestTextures()
            {
                // really need to clean this up
                // texture flags

                spheremap = (flag & (int)TextureFlags.SphereMap) > 0;

                aomap = (flag & (int)TextureFlags.StageAOMap) > 0 && !dummyramp;
                stagemap = (flag & (int)TextureFlags.StageAOMap) > 0 && dummyramp;

                cubemap = (flag & (int)TextureFlags.RampCubeMap) > 0 && (!dummyramp) && (!spheremap);
                ramp = (flag & (int) TextureFlags.RampCubeMap) > 0 && dummyramp; 

                diffuse = (flag & (int)TextureFlags.DiffuseMap) > 0;
                diffuse3 = (flag & 0x00009100) == 0x00009100 || (flag & 0x00009600) == 0x00009600 || (flag & 0x00009900) == 0x00009900; 
                diffuse2 = (flag & (int)TextureFlags.RampCubeMap) > 0 && (flag & (int)TextureFlags.NormalMap) == 0 
                    && dummyramp || diffuse3;

                normalmap = (flag & (int) TextureFlags.NormalMap) > 0;
            }
        }

        public class Polygon : TreeNode
        {
            public List<Vertex> vertices = new List<Vertex>();
            public List<int> faces = new List<int>();
            public int displayFaceSize = 0;

            // Material
            public List<Material> materials = new List<Material>();

            // for nud stuff
            public int vertSize = 0x46; // defaults to a basic bone weighted vertex format
            public int UVSize = 0x12;
            public int strip = 0x40;
            public int polflag = 0x04;

            // for drawing
            public bool isTransparent = false;
            public dVertex[] vertdata = new dVertex[3];
            public int[] display;
            public int[] selectedVerts;

            public Polygon()
            {
                Checked = true;
                ImageKey = "polygon";
                SelectedImageKey = "polygon";
            }

            public void AddVertex(Vertex v)
            {
                vertices.Add(v);
            }

            public void AOSpecRefBlend()
            {
                // change aomingain to only affect specular and reflection
                // ignore 2nd material
                if (materials[0].entries.ContainsKey("NU_aoMinGain"))
                {
                    materials[0].entries["NU_aoMinGain"][0] = 15.0f;
                    materials[0].entries["NU_aoMinGain"][1] = 15.0f;
                    materials[0].entries["NU_aoMinGain"][2] = 15.0f;
                    materials[0].entries["NU_aoMinGain"][3] = 0.0f;
                }      
            }

            public void PreRender()
            {

                // rearrange faces
                display = getDisplayFace().ToArray();

                List<dVertex> vert = new List<dVertex>();

                if (faces.Count <= 3)
                    return;
                foreach (Vertex v in vertices)
                {
                    dVertex nv = new dVertex()
                    {
                        pos = v.pos,
                        nrm = v.nrm,
                        tan = v.tan.Xyz,
                        bit = v.bitan.Xyz,
                        col = v.col / 0x7F, 
                        tx0 = v.tx.Count > 0 ? v.tx[0] : new Vector2(0, 0),
                        node = new Vector4(v.node.Count > 0 ? v.node[0] : -1,
                        v.node.Count > 1 ? v.node[1] : -1,
                        v.node.Count > 2 ? v.node[2] : -1,
                        v.node.Count > 3 ? v.node[3] : -1),
                        weight = new Vector4(v.weight.Count > 0 ? v.weight[0] : 0,
                        v.weight.Count > 1 ? v.weight[1] : 0,
                        v.weight.Count > 2 ? v.weight[2] : 0,
                        v.weight.Count > 3 ? v.weight[3] : 0),

                    };

                    isTransparent = false;
                    if (materials[0].srcFactor > 0 || materials[0].dstFactor > 0 || materials[0].AlphaFunc > 0 || materials[0].AlphaTest > 0)
                        isTransparent = true;

                    vert.Add(nv);
                }
                vertdata = vert.ToArray();
                vert = new List<dVertex>();
                selectedVerts = new int[vertdata.Length];
                }

            public void computeTangentBitangent()
            {
                List<int> f = getDisplayFace();
                Vector3[] tan1 = new Vector3[vertices.Count];
                Vector3[] tan2 = new Vector3[vertices.Count];
                for (int i = 0; i < displayFaceSize; i += 3)
                {
                    Vertex v1 = vertices[f[i]];
                    Vertex v2 = vertices[f[i + 1]];
                    Vertex v3 = vertices[f[i + 2]];

                    float x1 = v2.pos.X - v1.pos.X;
                    float x2 = v3.pos.X - v1.pos.X;
                    float y1 = v2.pos.Y - v1.pos.Y;
                    float y2 = v3.pos.Y - v1.pos.Y;
                    float z1 = v2.pos.Z - v1.pos.Z;
                    float z2 = v3.pos.Z - v1.pos.Z;

                    if (v2.tx.Count < 1) break;
                    float s1 = v2.tx[0].X - v1.tx[0].X;
                    float s2 = v3.tx[0].X - v1.tx[0].X;
                    float t1 = v2.tx[0].Y - v1.tx[0].Y;
                    float t2 = v3.tx[0].Y - v1.tx[0].Y;

                    float r = 1.0f;
                    // prevent incorrect tangent calculation from division by 0
                    float div = (s1 * t2 - s2 * t1);
                    if (div == 0)
                        r = 0.0f;
                    else
                        r = 1.0f / div;
                    Vector3 s = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r,
                        (t2 * z1 - t1 * z2) * r);
                    Vector3 t = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r,
                        (s1 * z2 - s2 * z1) * r);

                    tan1[f[i]] += s;
                    tan1[f[i + 1]] += s;
                    tan1[f[i + 2]] += s;

                    tan2[f[i]] += t;
                    tan2[f[i + 1]] += t;
                    tan2[f[i + 2]] += t;
                }

                for (int i = 0; i < vertices.Count; i++)
                {
                    Vertex v = vertices[i];
                    Vector3 t = tan1[i];

                    // orthogonalize tangent and calculate bitangent from tangent
                    v.tan = new Vector4(Vector3.Normalize(t - v.nrm * Vector3.Dot(v.nrm, t)), Vector3.Dot(Vector3.Cross(v.nrm, t), tan2[i]) < 0.0f ? -1.0f : 1.0f);
                    v.bitan = new Vector4(Vector3.Cross(v.tan.Xyz, v.nrm), 1.0f);
                }

                PreRender();

            }

            public void SmoothNormals()
            {
                Vector3[] normals = new Vector3[vertices.Count];

                for(int i = 0; i < normals.Length; i++)
                    normals[i] = new Vector3(0, 0, 0);

                List<int> f = getDisplayFace();

                for (int i = 0; i < displayFaceSize; i += 3)
                {
                    Vertex v1 = vertices[f[i]];
                    Vertex v2 = vertices[f[i+1]];
                    Vertex v3 = vertices[f[i+2]];
                    Vector3 nrm = CalculateNormal(v1,v2,v3);

                    normals[f[i + 0]] += nrm;// * a1;
                    normals[f[i + 1]] += nrm;// * a2;
                    normals[f[i + 2]] += nrm;// * a3;
                }
                
                for (int i = 0; i < normals.Length; i++)
                    vertices[i].nrm = normals[i].Normalized();

                foreach (Vertex v in vertices)
                {
                    foreach (Vertex v2 in vertices)
                    {
                        if (v == v2) continue;
                        float dis = (float)Math.Sqrt(Math.Pow(v.pos.X - v2.pos.X, 2) + Math.Pow(v.pos.Y - v2.pos.Y, 2) + Math.Pow(v.pos.Z - v2.pos.Z, 2));
                        if (dis <= 0f) // Extra smooth
                        {
                            Vector3 nn = ((v2.nrm + v.nrm)/2).Normalized();
                            v.nrm = nn;
                            v2.nrm = nn;
                        }
                    }
                }

                PreRender();
            }

            public void CalculateNormals()
            {
                Vector3[] normals = new Vector3[vertices.Count];

                for (int i = 0; i < normals.Length; i++)
                    normals[i] = new Vector3(0, 0, 0);

                List<int> f = getDisplayFace();

                for (int i = 0; i < displayFaceSize; i += 3)
                {
                    Vertex v1 = vertices[f[i]];
                    Vertex v2 = vertices[f[i + 1]];
                    Vertex v3 = vertices[f[i + 2]];
                    Vector3 nrm = CalculateNormal(v1, v2, v3);

                    normals[f[i + 0]] += nrm;// * a1;
                    normals[f[i + 1]] += nrm;// * a2;
                    normals[f[i + 2]] += nrm;// * a3;
                }

                for (int i = 0; i < normals.Length; i++)
                    vertices[i].nrm = normals[i].Normalized();

                PreRender();
            }

            private Vector3 CalculateNormal(Vertex v1, Vertex v2, Vertex v3)
            {
                Vector3 U = v2.pos - v1.pos;
                Vector3 V = v3.pos - v1.pos;

                return Vector3.Cross(U, V).Normalized();
            }

            public void setDefaultMaterial()
            {
                Material mat = new Material();
                mat.flags = 0x94010161;
                mat.cullMode = 0x0405;
                mat.entries.Add("NU_colorSamplerUV", new float[] { 1, 1, 0, 0 });
                mat.entries.Add("NU_fresnelColor", new float[] { 1, 1, 1, 1 });
                mat.entries.Add("NU_blinkColor", new float[] { 0, 0, 0, 0 });
                mat.entries.Add("NU_aoMinGain", new float[] { 0, 0, 0, 0 });
                mat.entries.Add("NU_lightMapColorOffset", new float[] { 0, 0, 0, 0 });
                mat.entries.Add("NU_fresnelParams", new float[] { 1, 0, 0, 0 });
                mat.entries.Add("NU_alphaBlendParams", new float[] { 0, 0, 0, 0 });
                mat.entries.Add("NU_materialHash", new float[] { FileData.toFloat(0x7E538F65), 0, 0, 0 });
                materials.Add(mat);

                // don't load 10080000 as default diffuse textureID to avoid displaying ramp as diffuse for imports
                Mat_Texture defaultDif = new Mat_Texture();
                defaultDif.WrapMode1 = 1;
                defaultDif.WrapMode2 = 1;
                defaultDif.minFilter = 3;
                defaultDif.magFilter = 2;
                defaultDif.mipDetail = 1;
                defaultDif.mipDetail = 6;
                defaultDif.hash = 0x10000000;

                mat.textures.Add(defaultDif);
                mat.textures.Add(makeDefault());
            }

            public static Mat_Texture makeDefault()
            {
                Mat_Texture dif = new Mat_Texture();
                dif.WrapMode1 = 1;
                dif.WrapMode2 = 1;
                dif.minFilter = 3;
                dif.magFilter = 2;
                dif.mipDetail = 1;
                dif.mipDetail = 6;
                dif.hash = (int) DummyTextures.DummyRamp;
                return dif;
            }

            public List<int> getDisplayFace()
            {
                if ((strip >> 4) == 4)
                {
                    displayFaceSize = faces.Count;
                    return faces;
                }
                else
                {
                    List<int> f = new List<int>();

                    int startDirection = 1;
                    int p = 0;
                    int f1 = faces[p++];
                    int f2 = faces[p++];
                    int faceDirection = startDirection;
                    int f3;
                    do
                    {
                        f3 = faces[p++];
                        if (f3 == 0xFFFF)
                        {
                            f1 = faces[p++];
                            f2 = faces[p++];
                            faceDirection = startDirection;
                        }
                        else
                        {
                            faceDirection *= -1;
                            if ((f1 != f2) && (f2 != f3) && (f3 != f1))
                            {
                                if (faceDirection > 0)
                                {
                                    f.Add(f3);
                                    f.Add(f2);
                                    f.Add(f1);
                                }
                                else
                                {
                                    f.Add(f2);
                                    f.Add(f3);
                                    f.Add(f1);
                                }
                            }
                            f1 = f2;
                            f2 = f3;
                        }
                    } while (p < faces.Count);

                    displayFaceSize = f.Count;
                    return f;
                }
            }
        }

        // typically a mesh will just have 1 polygon
        // but you can just use the mesh class without polygons
        public class Mesh : TreeNode
        {
            //public List<Polygon> polygons = new List<Polygon>();
            public int boneflag = 4; // 0 not rigged 4 rigged 8 singlebind
            public short singlebind = -1;
            
            public float[] boundingBox = new float[8];

            public Mesh()
            {
                Checked = true;
                ImageKey = "mesh";
                SelectedImageKey = "mesh";
            }

            public void addVertex(Vertex v)
            {
                if (Nodes.Count == 0)
                    Nodes.Add(new Polygon());

                ((Polygon)Nodes[0]).AddVertex(v);
            }
        }

        #endregion

        #region Converters

        public MBN toMBN()
        {
            MBN m = new Smash_Forge.MBN();

            m.setDefaultDescriptor();
            List<MBN.Vertex> vertBank = new List<MBN.Vertex>();

            foreach (Mesh mesh in mesh)
            {
                MBN.Mesh nmesh = new MBN.Mesh();

                int pi = 0;
                int fadd = vertBank.Count;
                nmesh.nodeList = new List<List<int>>();
                nmesh.faces = new List<List<int>>();
                foreach (Polygon p in mesh.Nodes)
                {
                    List<int> nodeList = new List<int>();
                    // vertices
                    foreach(Vertex v in p.vertices)
                    {
                        MBN.Vertex mv = new MBN.Vertex();
                        mv.pos = v.pos;
                        mv.nrm = v.nrm;
                        List<Vector2> uvs = new List<Vector2>();
                        uvs.Add(new Vector2(v.tx[0].X, 1 - v.tx[0].Y));
                        mv.tx = uvs;
                        mv.col = v.col;
                        int n1 = v.node[0];
                        int n2 = v.node.Count > 1 ? v.node[1] : 0;
                        if (!nodeList.Contains(n1)) nodeList.Add(n1);
                        if (!nodeList.Contains(n2)) nodeList.Add(n2);
                        mv.node.Add(nodeList.IndexOf(n1));
                        mv.node.Add(nodeList.IndexOf(n2));
                        mv.weight.Add(v.weight[0]);
                        mv.weight.Add(v.weight.Count > 1 ? v.weight[1] : 0);
                        vertBank.Add(mv);
                    }
                    // Node list 
                    nmesh.nodeList.Add(nodeList);
                    // polygons
                    List<int> fac = new List<int>();
                    nmesh.faces.Add(fac);
                    foreach (int i in p.faces)
                        fac.Add(i + fadd);
                    pi++;
                }

                m.mesh.Add(nmesh);
            }
            m.vertices = vertBank;

            //Console.WriteLine(m.vertices.Count + " " + m.descript.Count);

            return m;
        }

    
        public void Optimize(bool singleBind = false)
        {
            // to help with duplicates
            MergePoly();

            bool isSingleBound;
            int sbind;
            foreach (Mesh m in mesh)
            {
                isSingleBound = true;
                sbind = -1;
                foreach (Polygon p in m.Nodes)
                {
                    List<Vertex> nVert = new List<Vertex>();
                    List<int> nFace = new List<int>();

                    foreach (int f in p.faces)
                    {
                        int pos = -1; // nVert.IndexOf(p.vertices[f]);
                        int i = 0;
                        foreach (Vertex v in nVert)
                        {
                            if (v.node.Count > 0 && isSingleBound)
                            {
                                if (sbind == -1)
                                    sbind = p.vertices[f].node[0];
                                else
                                if (p.vertices[f].node[0] != sbind)
                                    isSingleBound = false;
                            }

                            if (v.Equals(p.vertices[f]))
                            {
                                pos = i;
                                break;
                            }
                            else
                                i++;
                        }

                        if (pos != -1)
                        {
                            nFace.Add(pos);
                        }
                        else
                        {
                            nVert.Add(p.vertices[f]);
                            nFace.Add(nVert.Count - 1);
                        }
                    }
                    p.vertices = nVert;
                    p.faces = nFace;
                    p.displayFaceSize = 0;
                }
                if (isSingleBound && singleBind)
                {
                    m.boneflag = 0x08; // single bind flag
                    m.singlebind = (short)sbind; // single bind bone
                    foreach (Polygon p in m.Nodes)
                    {
                        p.polflag = 0;
                        p.vertSize = p.vertSize & 0x0F;
                    }
                }
            }
            
            PreRender();
        }


        public void ComputeTangentBitangent()
        {
            foreach (Mesh m in mesh)
            {
                foreach (Polygon p in m.Nodes)
                    computeTangentBitangent(p);
            }
        }

        public static void computeTangentBitangent(Polygon p)
        {
            List<int> f = p.getDisplayFace();
            Vector3[] tan1 = new Vector3[p.vertices.Count];
            Vector3[] tan2 = new Vector3[p.vertices.Count];
            for (int i = 0; i < p.displayFaceSize; i += 3)
            {
                Vertex v1 = p.vertices[f[i]];
                Vertex v2 = p.vertices[f[i + 1]];
                Vertex v3 = p.vertices[f[i + 2]];

                float x1 = v2.pos.X - v1.pos.X;
                float x2 = v3.pos.X - v1.pos.X;
                float y1 = v2.pos.Y - v1.pos.Y;
                float y2 = v3.pos.Y - v1.pos.Y;
                float z1 = v2.pos.Z - v1.pos.Z;
                float z2 = v3.pos.Z - v1.pos.Z;

                if (v2.tx.Count < 1) break;
                float s1 = v2.tx[0].X - v1.tx[0].X;
                float s2 = v3.tx[0].X - v1.tx[0].X;
                float t1 = v2.tx[0].Y - v1.tx[0].Y;
                float t2 = v3.tx[0].Y - v1.tx[0].Y;

                float r = 1.0f;
                // prevent incorrect tangent calculation from division by 0
                float div = (s1 * t2 - s2 * t1);
                if (div == 0)
                    r = 0.0f;
                else
                    r = 1.0f / div;
                Vector3 s = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, 
                    (t2 * z1 - t1 * z2) * r);
                Vector3 t = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, 
                    (s1 * z2 - s2 * z1) * r);

                tan1[f[i]] += s;
                tan1[f[i + 1]] += s;
                tan1[f[i + 2]] += s;

                tan2[f[i]] += t;
                tan2[f[i + 1]] += t;
                tan2[f[i + 2]] += t;
            }

            for (int i = 0; i < p.vertices.Count; i++)
            {
                Vertex v = p.vertices[i];
                Vector3 t = tan1[i];

                // orthogonalize tangent and calculate bitangent from tangent
                v.tan = new Vector4(Vector3.Normalize(t - v.nrm * Vector3.Dot(v.nrm, t)), Vector3.Dot(Vector3.Cross(v.nrm, t), tan2[i]) < 0.0f ? -1.0f : 1.0f);
                v.bitan = new Vector4(Vector3.Cross(v.tan.Xyz, v.nrm), 1.0f);
            }

        }

        #endregion

        public List<int> GetTexIds()
        {
            List<int> texIds = new List<int>();
            foreach (var m in mesh)
                foreach (Polygon poly in m.Nodes)
                    foreach (var mat in poly.materials)
                        if(!texIds.Contains(mat.displayTexId))
                            texIds.Add(mat.displayTexId);
            return texIds;
        }
    }
}

