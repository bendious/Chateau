#!/usr/bin/python
# Save to 'AppData\Roaming\GIMP\2.10\plug-ins' to install as a menu option for GIMP2.1

from gimpfu import *
import math


def IsNotSelected(image, x, y):
	# TODO: option to wrap edges? take selection amount into account?
	return x < 0 or y < 0 or x >= image.width or y >= image.height or image.selection.get_pixel(x, y)[0] == 0

def EdgeRange(edgeWidth, stepSize):
	return range(1, int(round(edgeWidth / stepSize)) + 1)

def FalloffPct(stepPct, linearFalloff):
	return 1.0 - stepPct if linearFalloff else math.cos(stepPct * math.pi * 0.5)

def ColorizeFromCenter(image, x, y, pixel, edgeWidth, stepSize, centerX, centerY, linearFalloff, invert):
	diffX = x - centerX
	diffY = y - centerY
	diffMag = math.sqrt(diffX**2 + diffY**2)
	if (diffMag > 0.0):
		diffX /= diffMag
		diffY /= diffMag
	
	xItr = x
	yItr = y
	for i in EdgeRange(edgeWidth, stepSize):
		xItr += diffX * stepSize
		yItr += diffY * stepSize
		if (IsNotSelected(image, int(round(xItr, 0)), int(round(yItr, 0)))):
			pct = FalloffPct((i - 1) * stepSize / edgeWidth, linearFalloff)
			if (invert):
				pct = -pct;
			pixel[0] += diffX * pct
			pixel[1] -= diffY * pct # NOTE the -= due to Y=0 being the image top
			break;
	return pixel

def CheckEdge(image, x, y, pixel, xDiff, yDiff, invert):
	# TODO: take selection amount into account
	if (IsNotSelected(image, x, y)):
		pixel[0] += -xDiff if invert else xDiff
		pixel[1] -= -yDiff if invert else yDiff # NOTE the -= due to Y=0 being the image top
	return pixel

def SelectionNormalMap(image, layerOrChannel, edgeWidth, stepSize, fromCenter, centerX, centerY, linearFalloff, invert):
	# create new layer to copy to for safety
	layer = gimp.Layer(image, "selectionNormalMap", image.width, image.height, RGBA_IMAGE)
	
	for xLocal in range(layer.width):
		for yLocal in range(layer.height):
			x = xLocal + layer.offsets[0]
			y = yLocal + layer.offsets[1]
			if (IsNotSelected(image, x, y)):
				layer.set_pixel(xLocal, yLocal, (0, 0, 0, 0))
				continue
			
			# base vector is pure Z
			pixelFNeg = [0.0, 0.0, 1.0, 1.0]
			
			# nudge vector based on surrounding selection
			if (fromCenter):
				pixelFNeg = ColorizeFromCenter(image, x, y, pixelFNeg, edgeWidth, stepSize, centerX, centerY, linearFalloff, invert)
			else:
				# TODO: efficiency
				invSqrtTwo = 1.0 / math.sqrt(2.0)
				for i in EdgeRange(edgeWidth, stepSize):
					dFloat = i * stepSize
					d = int(round(dFloat, 0)) # TODO: round after applying to x/y?
					falloff = FalloffPct((dFloat - stepSize) / edgeWidth, linearFalloff)
					falloffDiagonal = falloff * invSqrtTwo
					
					pixelFNeg = CheckEdge(image, x - d, y, pixelFNeg, -falloff, 0.0, invert)
					pixelFNeg = CheckEdge(image, x + d, y, pixelFNeg, falloff, 0.0, invert)
					pixelFNeg = CheckEdge(image, x, y - d, pixelFNeg, 0.0, -falloff, invert)
					pixelFNeg = CheckEdge(image, x, y + d, pixelFNeg, 0.0, falloff, invert)
					pixelFNeg = CheckEdge(image, x - d, y - d, pixelFNeg, -falloffDiagonal, -falloffDiagonal, invert)
					pixelFNeg = CheckEdge(image, x + d, y + d, pixelFNeg, falloffDiagonal, falloffDiagonal, invert)
					pixelFNeg = CheckEdge(image, x - d, y + d, pixelFNeg, -falloffDiagonal, falloffDiagonal, invert)
					pixelFNeg = CheckEdge(image, x + d, y - d, pixelFNeg, falloffDiagonal, -falloffDiagonal, invert)
			
			# normalize and return to [0,255]
			pixelMag = math.sqrt(pixelFNeg[0]**2 + pixelFNeg[1]**2 + pixelFNeg[2]**2)
			pixelNormFNeg = (pixelFNeg[0] / pixelMag, pixelFNeg[1] / pixelMag, pixelFNeg[2] / pixelMag, min(1.0, pixelFNeg[3]))
			pixelNormF = (pixelNormFNeg[0] * 0.5 + 0.5, pixelNormFNeg[1] * 0.5 + 0.5, pixelNormFNeg[2] * 0.5 + 0.5, pixelNormFNeg[3])
			pixelNormI = (int(pixelNormF[0] * 255.0), int(pixelNormF[1] * 255.0), int(pixelNormF[2] * 255.0), int(pixelNormF[3] * 255.0))
			
			layer.set_pixel(xLocal, yLocal, pixelNormI)
	
	# TODO: blend pixels inward?
	
	# add layer
	image.add_layer(layer, 0)


# add to GIMP menu
register("SelectionNormalMap",
		"Estimates normal map for the selection based on selection shape.",
		"Estimates normal map for the selection based on selection shape.",
		"Benjamin Laws",
		"Benjamin Laws",
		"2022",
		"<Image>/Filters/Selection Normal Map",
		"RGB*",
		[
			(PF_FLOAT, "edgeWidth", "Edge Width", 1.0),
			(PF_FLOAT, "stepSize", "Step Size", 1.0),
			(PF_BOOL, "fromCenter", "From Center", False),
			(PF_FLOAT, "centerX", "Center X", 0.0),
			(PF_FLOAT, "centerY", "Center Y", 0.0),
			(PF_BOOL, "linearFalloff", "Linear Falloff", False),
			(PF_BOOL, "invert", "Invert Directions", False),
		],
		[],
		SelectionNormalMap)
main()
