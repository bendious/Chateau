#!/usr/bin/python
# Save to 'AppData\Roaming\GIMP\2.10\plug-ins' to install as a menu option for GIMP2.1

from gimpfu import *
import math


def TextureNormalize(image, layerOrChannel):
	# create new layer to copy to for safety
	layer = gimp.Layer(image, "normalized", layerOrChannel.width, layerOrChannel.height, RGBA_IMAGE)
	
	# TODO: handle differences between layer/image positioning/size
	for x in range(0, layer.width):
		for y in range(0, layer.height):
			pixelI = layerOrChannel.get_pixel(x, y)
			
			# convert from [0,255] to [0.0,1.0] and get magnitude
			pixelF = (pixelI[0] / 255.0, pixelI[1] / 255.0, pixelI[2] / 255.0)
			pixelFNeg = (pixelF[0] * 2.0 - 1.0, pixelF[1] * 2.0 - 1.0, pixelF[2] * 2.0 - 1.0)
			pixelMag = math.sqrt(pixelFNeg[0]**2 + pixelFNeg[1]**2 + pixelFNeg[2]**2)
			
			# normalize and return to [0,255]
			pixelNormFNeg = (pixelFNeg[0] / pixelMag, pixelFNeg[1] / pixelMag, pixelFNeg[2] / pixelMag)
			pixelNormF = (pixelNormFNeg[0] * 0.5 + 0.5, pixelNormFNeg[1] * 0.5 + 0.5, pixelNormFNeg[2] * 0.5 + 0.5)
			pixelNormI = (int(pixelNormF[0] * 255.0), int(pixelNormF[1] * 255.0), int(pixelNormF[2] * 255.0), pixelI[3])
			
			layer.set_pixel(x, y, pixelNormI)
	
	# add layer
	image.add_layer(layer, 0)


# add to GIMP menu
register("TextureNormalize",
		"Normalizes the length of each RGB tuple for use as a normal map.",
		"Normalizes the length of each RGB tuple for use as a normal map.",
		"Benjamin Laws",
		"Benjamin Laws",
		"2022",
		"<Image>/Filters/Normal Map Normalize",
		"RGB*",
		[],
		[],
		TextureNormalize)
main()
