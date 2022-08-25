#!/usr/bin/python
# Save to 'AppData\Roaming\GIMP\2.10\plug-ins' to install as a menu option for GIMP2.1

from gimpfu import *
import math


def IsNotSelected(image, layer, x, y):
	# TODO: option to wrap edges?
	return x < 0 or y < 0 or x >= layer.width or y >= layer.height or image.selection.get_pixel(x, y)[0] < 128

def CheckEdge(image, layer, x, y, pixel, xDiff, yDiff, opacity):
	# TODO: take selection amount into account
	if (IsNotSelected(image, layer, x, y)):
		pixel[0] += xDiff
		pixel[1] -= yDiff # NOTE the -= due to Y=0 being the image top
		pixel[3] += opacity
	return pixel

def SelectionNormalMap(image, layerOrChannel, opacity):
	# create new layer to copy to for safety
	layer = gimp.Layer(image, "selectionNormalMap", layerOrChannel.width, layerOrChannel.height, RGBA_IMAGE)
	
	for x in range(0, layer.width):
		for y in range(0, layer.height):
			if (IsNotSelected(image, layer, x, y)):
				layer.set_pixel(x, y, (0, 0, 0, 0))
				continue
			
			# base vector is pure Z
			pixelFNeg = [0.0, 0.0, 1.0, 0.0]
			
			# nudge vector based on surrounding selection
			pixelFNeg = CheckEdge(image, layer, x - 1, y, pixelFNeg, -1.0, 0.0, opacity)
			pixelFNeg = CheckEdge(image, layer, x + 1, y, pixelFNeg, 1.0, 0.0, opacity)
			pixelFNeg = CheckEdge(image, layer, x, y - 1, pixelFNeg, 0.0, -1.0, opacity)
			pixelFNeg = CheckEdge(image, layer, x, y + 1, pixelFNeg, 0.0, 1.0, opacity)
			pixelFNeg = CheckEdge(image, layer, x - 1, y - 1, pixelFNeg, -0.5, -0.5, opacity)
			pixelFNeg = CheckEdge(image, layer, x + 1, y + 1, pixelFNeg, 0.5, 0.5, opacity)
			pixelFNeg = CheckEdge(image, layer, x - 1, y + 1, pixelFNeg, -0.5, 0.5, opacity)
			pixelFNeg = CheckEdge(image, layer, x + 1, y - 1, pixelFNeg, 0.5, -0.5, opacity)
			
			# normalize and return to [0,255]
			pixelMag = math.sqrt(pixelFNeg[0]**2 + pixelFNeg[1]**2 + pixelFNeg[2]**2)
			pixelNormFNeg = (pixelFNeg[0] / pixelMag, pixelFNeg[1] / pixelMag, pixelFNeg[2] / pixelMag, min(1.0, pixelFNeg[3]))
			pixelNormF = (pixelNormFNeg[0] * 0.5 + 0.5, pixelNormFNeg[1] * 0.5 + 0.5, pixelNormFNeg[2] * 0.5 + 0.5, pixelNormFNeg[3])
			pixelNormI = (int(pixelNormF[0] * 255.0), int(pixelNormF[1] * 255.0), int(pixelNormF[2] * 255.0), int(pixelNormF[3] * 255.0))
			
			layer.set_pixel(x, y, pixelNormI)
	
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
			(PF_FLOAT, "opacity", "Opacity", 0.3)
		],
		[],
		SelectionNormalMap)
main()
