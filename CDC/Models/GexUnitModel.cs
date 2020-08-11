using System;
using System.IO;
using System.Collections.Generic;

namespace CDC.Objects.Models
{
    public class GexUnitModel : GexModel
    {
        protected UInt32 m_uBspTreeCount;
        protected UInt32 m_uBspTreeStart;
        protected UInt32 m_uSpectralVertexStart;
        protected UInt32 m_uSpectralColourStart;

        public GexUnitModel(BinaryReader xReader, UInt32 uDataStart, UInt32 uModelData, String strModelName, Platform ePlatform, UInt32 uVersion)
            : base(xReader, uDataStart, uModelData, strModelName, ePlatform, uVersion)
        {
            xReader.BaseStream.Position = _modelData + 0x10;
            _vertexCount = xReader.ReadUInt32();
            _polygonCount = xReader.ReadUInt32();
            xReader.BaseStream.Position += 0x04;
            _vertexStart = _dataStart + xReader.ReadUInt32();
            _polygonStart = _dataStart + xReader.ReadUInt32();
            xReader.BaseStream.Position += 0x10;
            _materialStart = _dataStart + xReader.ReadUInt32();
            _materialCount = 0;

            xReader.BaseStream.Position += 0x0C;

            m_uSpectralVertexStart = _dataStart + xReader.ReadUInt32();
            m_uSpectralColourStart = _dataStart + xReader.ReadUInt32();
            m_uBspTreeCount = xReader.ReadUInt32();
            m_uBspTreeStart = _dataStart + xReader.ReadUInt32();
            _groupCount = m_uBspTreeCount;

            _trees = new Tree[_groupCount];
        }

        public static GexUnitModel Load(BinaryReader xReader, UInt32 uDataStart, UInt32 uModelData, String strModelName, Platform ePlatform, UInt32 uVersion, CDC.Objects.ExportOptions options)
        {
            GexUnitModel xModel = new GexUnitModel(xReader, uDataStart, uModelData, strModelName, ePlatform, uVersion);
            xModel.ReadData(xReader, options);
            return xModel;
        }

        protected override void ReadVertex(BinaryReader xReader, int v, CDC.Objects.ExportOptions options)
        {
            base.ReadVertex(xReader, v, options);

            _geometry.PositionsPhys[v] = _geometry.PositionsRaw[v];
            _geometry.PositionsAltPhys[v] = _geometry.PositionsPhys[v];

            _geometry.Vertices[v].colourID = v;

            xReader.BaseStream.Position += 2;
            uint vColour = xReader.ReadUInt32() | 0xFF000000;
            if (options.IgnoreVertexColours)
            {
                _geometry.Colours[v] = 0xFFFFFFFF;
            }
            else
            {
                _geometry.Colours[v] = vColour;
            }

            Utility.FlipRedAndBlue(ref _geometry.Colours[v]);

            _geometry.ColoursAlt[v] = _geometry.Colours[v];
        }

        protected override void ReadVertices(BinaryReader xReader, CDC.Objects.ExportOptions options)
        {
            base.ReadVertices(xReader, options);

            ReadSpectralData(xReader, options);
        }

        protected virtual void ReadSpectralData(BinaryReader xReader, CDC.Objects.ExportOptions options)
        {
            if (m_uSpectralColourStart != 0)
            {
                // Spectral Colours
                xReader.BaseStream.Position = m_uSpectralColourStart;
                for (int v = 0; v < _vertexCount; v++)
                {
                    UInt32 uShiftColour = xReader.ReadUInt16();
                    UInt32 uAlpha = _geometry.ColoursAlt[v] & 0xFF000000;
                    UInt32 uRed = ((uShiftColour >> 0) & 0x1F) << 0x13;
                    UInt32 uGreen = ((uShiftColour >> 5) & 0x1F) << 0x0B;
                    UInt32 uBlue = ((uShiftColour >> 10) & 0x1F) << 0x03;
                    _geometry.ColoursAlt[v] = uAlpha | uRed | uGreen | uBlue;
                }
            }

            if (m_uSpectralVertexStart != 0)
            {
                // Spectral Verticices
                xReader.BaseStream.Position = m_uSpectralVertexStart + 0x06;
                int sVertex = xReader.ReadInt16();
                xReader.BaseStream.Position = m_uSpectralVertexStart;
                while (sVertex != 0xFFFF)
                {
                    ShiftVertex xShiftVertex;
                    xShiftVertex.basePos.x = (float)xReader.ReadInt16();
                    xShiftVertex.basePos.y = (float)xReader.ReadInt16();
                    xShiftVertex.basePos.z = (float)xReader.ReadInt16();
                    sVertex = xReader.ReadUInt16();

                    if (sVertex == 0xFFFF)
                    {
                        break;
                    }

                    xShiftVertex.offset.x = (float)xReader.ReadInt16();
                    xShiftVertex.offset.y = (float)xReader.ReadInt16();
                    xShiftVertex.offset.z = (float)xReader.ReadInt16();
                    _geometry.PositionsAltPhys[sVertex] = xShiftVertex.offset + xShiftVertex.basePos;
                }
            }
        }

        protected virtual void ReadPolygon(BinaryReader xReader, int p, CDC.Objects.ExportOptions options)
        {
            UInt32 uPolygonPosition = (UInt32)xReader.BaseStream.Position;

            _polygons[p].v1 = _geometry.Vertices[xReader.ReadUInt16()];
            _polygons[p].v2 = _geometry.Vertices[xReader.ReadUInt16()];
            _polygons[p].v3 = _geometry.Vertices[xReader.ReadUInt16()];
            _polygons[p].material = new Material();

            _polygons[p].material.textureUsed |= (Boolean)(((int)xReader.ReadUInt16() & 0x0004) == 0);
            xReader.BaseStream.Position += 0x02;
            UInt16 uMaterialOffset = xReader.ReadUInt16();
            _polygons[p].material.textureUsed &= (Boolean)(uMaterialOffset != 0xFFFF);

            if (_polygons[p].material.textureUsed)
            {
                UInt32 uMaterialPosition = uMaterialOffset + _materialStart;

                xReader.BaseStream.Position = uMaterialPosition;
                ReadMaterial(xReader, p, options);
            }
            else
            {
                _polygons[p].material.textureUsed = false;
                _polygons[p].material.colour = 0xFFFFFFFF;
            }

            Utility.FlipRedAndBlue(ref _polygons[p].material.colour);

            xReader.BaseStream.Position = uPolygonPosition + 0x0C;
        }

        protected override void ReadPolygons(BinaryReader xReader, CDC.Objects.ExportOptions options)
        {
            if (_polygonStart == 0 || _polygonCount == 0)
            {
                return;
            }

            xReader.BaseStream.Position = _polygonStart;

            for (UInt16 p = 0; p < _polygonCount; p++)
            {
                ReadPolygon(xReader, p, options);
            }

            List<Mesh> xMeshes = new List<Mesh>();
            List<int> xMeshPositions = new List<int>();
            List<UInt32> treePolygons = new List<UInt32>((Int32)_vertexCount * 3);

            for (UInt32 t = 0; t < m_uBspTreeCount; t++)
            {
                xReader.BaseStream.Position = m_uBspTreeStart + (t * 0x24);
                UInt32 uDataPos = _dataStart + xReader.ReadUInt32();
                xReader.BaseStream.Position += 0x0C;
                bool drawTester = ((xReader.ReadInt32() & 1) != 1);
                xReader.BaseStream.Position += 0x06;
                UInt16 usBspID = xReader.ReadUInt16();

                _trees[t] = ReadBSPTree(xReader, treePolygons, uDataPos, _trees[t], xMeshes, xMeshPositions, 0);
            }

            MaterialList xMaterialsList = null;

            for (UInt16 p = 0; p < _polygonCount; p++)
            {
                if (xMaterialsList == null)
                {
                    xMaterialsList = new MaterialList(_polygons[p].material);
                    _materialsList.Add(_polygons[p].material);
                }
                else
                {
                    Material newMaterial = xMaterialsList.AddToList(_polygons[p].material);
                    if (_polygons[p].material != newMaterial)
                    {
                        _polygons[p].material = newMaterial;
                    }
                    else
                    {
                        _materialsList.Add(_polygons[p].material);
                    }
                }
            }

            _materialCount = (UInt32)_materialsList.Count;

            int currentPosition = 0;
            for (int m = 0; m < xMeshes.Count; m++)
            {
                FinaliseMesh(treePolygons, currentPosition, xMeshes[m]);
                currentPosition = xMeshPositions[m];
            }
        }

        protected virtual Tree ReadBSPTree(BinaryReader xReader, List<UInt32> treePolygons, UInt32 uDataPos, Tree xParentTree, List<Mesh> xMeshes, List<int> xMeshPositions, UInt32 uDepth)
        {
            if (uDataPos == 0)
            {
                return null;
            }

            xReader.BaseStream.Position = uDataPos + 0x0E;
            bool isLeaf = ((xReader.ReadByte() & 0x02) == 0x02);
            Int32 iSubTreeCount = 2;

            Tree xTree = null;
            Mesh xMesh = null;

            UInt32 uMaxDepth = 0;

            if (uDepth <= uMaxDepth)
            {
                xTree = new Tree();
                xMesh = new Mesh();
                xMesh.startIndex = 0;
                xTree.mesh = xMesh;

                if (xParentTree != null)
                {
                    xParentTree.Push(xTree);
                }
            }
            else
            {
                xTree = xParentTree;
                xMesh = xParentTree.mesh;
            }

            if (isLeaf)
            {
                xTree.isLeaf = true;

                xReader.BaseStream.Position = uDataPos + 0x08;
                ReadBSPLeaf(xReader, treePolygons, xMesh);
            }
            else
            {
                xReader.BaseStream.Position = uDataPos + 0x14;

                UInt32[] auSubTreePositions = new UInt32[2];
                for (Int32 s = 0; s < iSubTreeCount; s++)
                {
                    auSubTreePositions[s] = xReader.ReadUInt32();
                }

                for (Int32 s = 0; s < iSubTreeCount; s++)
                {
                    ReadBSPTree(xReader, treePolygons, auSubTreePositions[s], xTree, xMeshes, xMeshPositions, uDepth + 1);
                }
            }

            if (uDepth <= uMaxDepth)
            {
                if (xMesh != null && xMesh.indexCount > 0)
                {
                    xMeshes.Add(xMesh);
                    xMeshPositions.Add(treePolygons.Count);
                }
            }

            return xTree;
        }

        protected virtual void ReadBSPLeaf(BinaryReader xReader, List<UInt32> treePolygons, Mesh xMesh)
        {
            UInt32 polygonPos = _dataStart + xReader.ReadUInt32();
            UInt32 polygonID = (polygonPos - _polygonStart) / 0x0C;
            UInt16 polyCount = xReader.ReadUInt16();
            for (UInt16 p = 0; p < polyCount; p++)
            {
                _polygons[polygonID + p].material.visible = true;

                treePolygons.Add(polygonID + p);

                if (xMesh != null)
                {
                    xMesh.indexCount += 3;
                }
            }
        }

        protected virtual void FinaliseMesh(List<UInt32> treePolygons, int firstPolygon, Mesh xMesh)
        {
            xMesh.polygonCount = xMesh.indexCount / 3;
            xMesh.polygons = new Polygon[xMesh.polygonCount];
            for (int p = 0; p < xMesh.polygonCount; p++)
            {
                UInt32 polygonID = treePolygons[firstPolygon + p];
                xMesh.polygons[p] = _polygons[polygonID];
            }

            xMesh.vertices = new Vertex[xMesh.indexCount];
            for (UInt16 poly = 0; poly < xMesh.polygonCount; poly++)
            {
                xMesh.vertices[(3 * poly) + 0] = xMesh.polygons[poly].v1;
                xMesh.vertices[(3 * poly) + 1] = xMesh.polygons[poly].v2;
                xMesh.vertices[(3 * poly) + 2] = xMesh.polygons[poly].v3;
            }
        }
    }
}